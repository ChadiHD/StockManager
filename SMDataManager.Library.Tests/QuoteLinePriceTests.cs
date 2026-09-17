using FluentAssertions;
using Microsoft.Data.SqlClient;
using SMDataManager.Library.Pricing;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// What price a quote line ends up holding, which is not always the price the caller sent.
/// </summary>
/// <remarks>
/// <see cref="CatalogPriceParityTests"/> pins the catalog's SQL sort key to
/// <see cref="PriceResolver"/>. This pins the other half of the same rule: the price a customer
/// was shown is the price that gets written down. Until T5 <c>spQuoteLine_Insert</c> derived
/// <c>NetPrice</c> unconditionally from an integer discount, which was right for an admin
/// typing numbers into the portal and wrong for anything carrying a resolved price — a floored
/// price has no integer discount that reproduces it, and the old expression did not round at
/// all, so <c>money</c>'s four decimal places reached the quote document.
///
/// Against a real database for the usual reason: the thing under test is the procedure's
/// arithmetic, and a C# reimplementation of it would be a third copy of
/// <see cref="PriceResolver"/>.
///
/// Skips without SMDATABASE_TEST_CONNECTION; see <see cref="TestDatabase"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class QuoteLinePriceTests
{
    private readonly IPriceResolver _resolver = new PriceResolver();

    /// <summary>
    /// List prices chosen so the derivation has somewhere to go wrong: a whole number, two
    /// that produce a third decimal place under an ordinary discount, one carrying a half-cent
    /// of its own, and a large one where a rounding difference is still one cent.
    /// </summary>
    private static readonly decimal[] ListPrices =
        [100.00m, 99.99m, 33.33m, 10.005m, 2499.00m, 0.00m, 1.00m];

    private static readonly decimal[] Discounts =
        [0m, 1m, 7m, 12.50m, 33.33m, 50m, 99m, 100m];

    [SkippableFact]
    public void ADerivedNetPriceAgreesWithPriceResolverToTheCent()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = QuoteLineFixture.Create(connection, transaction);

            var mismatches = new List<string>();

            foreach (var list in ListPrices)
            foreach (var discount in Discounts)
            {
                var stored = fixture.AddLine(list, discount, netPrice: null);

                // No cost and no margin, so PriceResolver reduces to exactly what the
                // procedure derives — list less the discount, rounded away from zero. That is
                // the comparison worth making: the floor cannot bind here, so any difference
                // is a difference in the arithmetic itself.
                var expected = _resolver.Resolve(list, cost: null, discount, minMarginPct: 0m).NetPrice;

                if (stored != expected)
                {
                    mismatches.Add($"list={list} discount={discount}: sql={stored} resolver={expected}");
                }
            }

            mismatches.Should().BeEmpty(
                "{0} of {1} combinations disagree between spQuoteLine_Insert and PriceResolver:{2}{3}",
                mismatches.Count,
                ListPrices.Length * Discounts.Length,
                Environment.NewLine,
                string.Join(Environment.NewLine, mismatches));
        }
    }

    [SkippableFact]
    public void ADerivedNetPriceIsRoundedToACurrencyAmount()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = QuoteLineFixture.Create(connection, transaction);

            // The concrete regression. money holds four decimal places and the old expression
            // did no rounding, so 99.99 at 7% stored 92.9907 while the page showed 92.99 — a
            // price nobody can pay, on the document the customer signs.
            fixture.AddLine(99.99m, 7m, netPrice: null).Should().Be(92.99m);
        }
    }

    [SkippableFact]
    public void AStatedNetPriceIsStoredExactlyAndNotRecomputed()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = QuoteLineFixture.Create(connection, transaction);

            // A price held up by the site's margin floor: cost 95 at a 12% floor is 106.40,
            // which no discount off a list of 100 produces. The storefront has to be able to
            // record it, so the procedure stores what it is given rather than overruling it.
            fixture.AddLine(100.00m, 12.50m, netPrice: 106.40m).Should().Be(106.40m);
        }
    }

    [SkippableFact]
    public void AFractionalDiscountSurvivesBeingStored()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = QuoteLineFixture.Create(connection, transaction);

            fixture.AddLine(200.00m, 12.50m, netPrice: 175.00m);

            // The column was INT until T5. A floored line recorded there would print a
            // discount that does not produce the price beside it.
            fixture.LastDiscount().Should().Be(12.50m);
        }
    }

    /// <summary>One quote in a throwaway store, ready to take lines.</summary>
    private sealed class QuoteLineFixture
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly int _siteId;
        private readonly int _quoteId;
        private readonly int _productId;

        private QuoteLineFixture(
            SqlConnection connection, SqlTransaction transaction,
            int siteId, int quoteId, int productId)
        {
            _connection = connection;
            _transaction = transaction;
            _siteId = siteId;
            _quoteId = quoteId;
            _productId = productId;
        }

        public static QuoteLineFixture Create(SqlConnection connection, SqlTransaction transaction)
        {
            var runId = Guid.NewGuid().ToString("N")[..12];

            var siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Quote line test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);
                """,
                ("@key", $"qline-{runId}"),
                ("@domain", $"{runId}.qline.invalid"));

            var productId = Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                         QuantityInStock, Published, Delisted)
                OUTPUT INSERTED.Id
                VALUES (N'Quote line fixture', N'Quote line fixture.', 100, @sku, N'QuoteLine',
                        5, 1, 0);
                """,
                ("@sku", $"QL-{runId}"));

            var accountId = Scalar(connection, transaction, """
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                         PaymentTerms, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, N'Quote line test customer', N'EUR', N'Card',
                        N'Prepaid', 0, N'Approved', @siteId);
                """,
                ("@reference", $"AC-L{runId[..6]}"),
                ("@siteId", siteId));

            var quoteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, [Status], SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, @accountId, N'EUR', N'Requested', @siteId);
                """,
                ("@reference", $"QT-L{runId[..6]}"),
                ("@accountId", accountId),
                ("@siteId", siteId));

            return new QuoteLineFixture(connection, transaction, siteId, quoteId, productId);
        }

        /// <summary>Adds a line through the procedure and returns the net price it stored.</summary>
        public decimal AddLine(decimal listPrice, decimal discountPct, decimal? netPrice)
        {
            using var command = new SqlCommand("""
                DECLARE @LineId int;

                EXEC dbo.spQuoteLine_Insert
                    @Id = @LineId OUTPUT,
                    @QuoteId = @quoteId, @ProductId = @productId, @Quantity = 1,
                    @ListPrice = @listPrice, @DiscountPct = @discountPct,
                    @SiteId = @siteId, @NetPrice = @netPrice;

                SELECT [NetPrice] FROM dbo.QuoteLine WHERE [Id] = @LineId;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", _quoteId);
            command.Parameters.AddWithValue("@productId", _productId);
            command.Parameters.AddWithValue("@siteId", _siteId);
            command.Parameters.AddWithValue("@listPrice", listPrice);
            command.Parameters.AddWithValue("@discountPct", discountPct);
            command.Parameters.AddWithValue("@netPrice", (object?)netPrice ?? DBNull.Value);

            return (decimal)command.ExecuteScalar()!;
        }

        public decimal LastDiscount()
        {
            using var command = new SqlCommand("""
                SELECT TOP (1) [DiscountPct] FROM dbo.QuoteLine
                WHERE [QuoteId] = @quoteId ORDER BY [Id] DESC;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", _quoteId);

            return (decimal)command.ExecuteScalar()!;
        }

        private static int Scalar(
            SqlConnection connection, SqlTransaction transaction, string sql,
            params (string Name, object Value)[] parameters)
        {
            using var command = new SqlCommand(sql, connection, transaction);

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (int)command.ExecuteScalar()!;
        }
    }
}
