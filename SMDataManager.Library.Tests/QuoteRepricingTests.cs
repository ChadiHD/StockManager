using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Re-pricing a quote: editing a line, and sending the result to the customer.
/// </summary>
/// <remarks>
/// Against a real database because the properties are database properties. Both procedures are
/// predicates — one refuses a line that is not on this quote, or on a quote already accepted;
/// the other is an atomic claim over the statuses a quote may still be priced from — and a
/// substituted data access layer agrees with whatever the caller asserts.
///
/// Neither procedure opens a transaction, so the 50033 test is safe anywhere in its scope; see
/// <see cref="QuoteAcceptanceTests"/> for the case where that distinction matters.
///
/// Skips without SMDATABASE_TEST_CONNECTION; see <see cref="TestDatabase"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class QuoteRepricingTests
{
    /// <summary>The number spQuoteLine_Update THROWs for a quantity below one.</summary>
    private const int QuantityBelowOne = 50033;

    [SkippableFact]
    public void AnEditStoresTheQuantityAndDerivesTheNetPriceFromTheDiscount()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = RepricingFixture.Create(connection, transaction);

            fixture.Update(quantity: 5, discountPct: 7m).Should().Be(1);

            var line = fixture.Line();

            line.Quantity.Should().Be(5);
            line.DiscountPct.Should().Be(7m);

            // The same regression QuoteLinePriceTests pins on the insert path: 99.99 at 7% is
            // 92.9907 before rounding, and money carries four decimal places, so an
            // unrounded derivation reaches the quote document as a price nobody can pay.
            line.NetPrice.Should().Be(92.99m);
        }
    }

    [SkippableFact]
    public void AStatedNetPriceIsStoredRatherThanDerivedFromTheDiscount()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = RepricingFixture.Create(connection, transaction);

            // A price held up by Site.MinMarginPct has no discount that reproduces it, which
            // is why the parameter exists at all — see spQuoteLine_Insert.
            fixture.Update(quantity: 2, discountPct: 7m, netPrice: 88.88m).Should().Be(1);

            var line = fixture.Line();

            line.NetPrice.Should().Be(88.88m);
            line.DiscountPct.Should().Be(7m);
        }
    }

    [SkippableFact]
    public void ALineBelongingToAnotherQuoteIsNotRepriced()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = RepricingFixture.Create(connection, transaction);

            int strangerLine = fixture.AddSecondQuoteWithALine();

            // The quote is part of the predicate rather than something the caller checked, so
            // a line id from somewhere else addresses nothing.
            fixture.Update(quantity: 9, discountPct: 50m, lineId: strangerLine).Should().Be(0);

            fixture.LineById(strangerLine).Quantity.Should().Be(3);
        }
    }

    [SkippableFact]
    public void AnotherStoresQuoteCannotBeRepriced()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = RepricingFixture.Create(connection, transaction);

            fixture.Update(quantity: 9, discountPct: 50m, siteId: fixture.SiteId + 1000)
                .Should().Be(0);

            fixture.Line().Quantity.Should().Be(3);
        }
    }

    [SkippableFact]
    public void AnAcceptedQuotesLinesCannotBeRepriced()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = RepricingFixture.Create(connection, transaction);

            fixture.SetStatus("Accepted");

            // spOrder_ConvertFromQuote has already copied these lines onto a Purchase. A
            // re-price here would leave the quote and the order the customer accepted
            // disagreeing about what was sold, in money, with nothing recording which moved.
            fixture.Update(quantity: 9, discountPct: 50m).Should().Be(0);

            var line = fixture.Line();

            line.Quantity.Should().Be(3);
            line.NetPrice.Should().Be(90m);
        }
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(-4)]
    public void AQuantityBelowOneIsRefused(int quantity)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = RepricingFixture.Create(connection, transaction);

            var update = () => fixture.Update(quantity, discountPct: 10m);

            // Thrown rather than reported, because unlike "somebody else decided it first"
            // there is no state of the world in which this is the ordinary case.
            update.Should().Throw<SqlException>()
                .Which.Number.Should().Be(QuantityBelowOne);

            fixture.Line().Quantity.Should().Be(3);
        }
    }

    [SkippableTheory]
    [InlineData("Requested")]
    [InlineData("Priced")]
    public void SendingToTheCustomerPricesAQuoteThatIsStillUndecided(string from)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = RepricingFixture.Create(connection, transaction);

            fixture.SetStatus(from);

            // Priced passes as well as Requested: re-sending after editing lines is ordinary
            // work, and refusing the second press would leave an operator wondering which of
            // the two writes landed.
            fixture.Price().Should().Be(1);
            fixture.Status().Should().Be("Priced");
        }
    }

    [SkippableTheory]
    [InlineData("Accepted")]
    [InlineData("Rejected")]
    public void PricingCannotUndoADecisionTheCustomerAlreadyMade(string decided)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = RepricingFixture.Create(connection, transaction);

            fixture.SetStatus(decided);

            // The whole reason this is a claim and not spQuote_UpdateStatus with a string:
            // that procedure has no predicate on the current status, so pricing a quote the
            // customer had just rejected would silently un-reject it.
            fixture.Price().Should().Be(0);
            fixture.Status().Should().Be(decided);
        }
    }

    [SkippableFact]
    public void AnotherStoresQuoteCannotBePriced()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = RepricingFixture.Create(connection, transaction);

            fixture.SetStatus("Requested");

            fixture.Price(siteId: fixture.SiteId + 1000).Should().Be(0);
            fixture.Status().Should().Be("Requested");
        }
    }

    /// <summary>
    /// One store, one account, one quote at 3 x 100 less 10%, and a product to hang it on.
    /// </summary>
    private sealed class RepricingFixture
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly int _productId;
        private readonly string _runId;

        private RepricingFixture(
            SqlConnection connection, SqlTransaction transaction,
            int siteId, int accountId, int quoteId, int lineId, int productId, string runId)
        {
            _connection = connection;
            _transaction = transaction;
            _productId = productId;
            _runId = runId;
            SiteId = siteId;
            AccountId = accountId;
            QuoteId = quoteId;
            LineId = lineId;
        }

        public int SiteId { get; }
        public int AccountId { get; }
        public int QuoteId { get; }
        public int LineId { get; }

        public static RepricingFixture Create(SqlConnection connection, SqlTransaction transaction)
        {
            // SiteKey and Domain are unique, and a rolled-back transaction still collides with
            // a concurrent one that has not rolled back yet.
            var runId = Guid.NewGuid().ToString("N")[..12];

            var siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Repricing test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);
                """,
                ("@key", $"reprice-{runId}"),
                ("@domain", $"{runId}.reprice.invalid"));

            // 99.99 rather than a round number: the derivation has to round, and a list price
            // that divides cleanly would pass whether it rounded or not.
            var productId = Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                         QuantityInStock, Published, Delisted)
                OUTPUT INSERTED.Id
                VALUES (N'Repricing fixture', N'Repricing fixture.', 99.99, @sku, N'Repricing',
                        5, 1, 0);
                """,
                ("@sku", $"RPR-{runId}"));

            var accountId = Scalar(connection, transaction, """
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                         PaymentTerms, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, N'Repricing test customer', N'EUR', N'Credit',
                        N'Net 30', 10000, N'Approved', @siteId);
                """,
                ("@reference", $"AC-R{runId[..6]}"),
                ("@siteId", siteId));

            var quoteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, [Status], ExpiresDate, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, @accountId, N'EUR', N'Requested', @expires, @siteId);
                """,
                ("@reference", $"QT-R{runId[..6]}"),
                ("@accountId", accountId),
                ("@expires", DateTime.UtcNow.AddDays(14)),
                ("@siteId", siteId));

            var lineId = Scalar(connection, transaction, """
                INSERT INTO dbo.QuoteLine (QuoteId, ProductId, Quantity, ListPrice, DiscountPct, NetPrice)
                OUTPUT INSERTED.Id
                VALUES (@quoteId, @productId, 3, 99.99, 10, 90);
                """,
                ("@quoteId", quoteId),
                ("@productId", productId));

            return new RepricingFixture(
                connection, transaction, siteId, accountId, quoteId, lineId, productId, runId);
        }

        /// <summary>A second quote in the same store, so a stray line id has somewhere to live.</summary>
        public int AddSecondQuoteWithALine()
        {
            var otherQuote = Scalar(_connection, _transaction, """
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, [Status], SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, @accountId, N'EUR', N'Requested', @siteId);
                """,
                ("@reference", $"QT-S{_runId[..6]}"),
                ("@accountId", AccountId),
                ("@siteId", SiteId));

            return Scalar(_connection, _transaction, """
                INSERT INTO dbo.QuoteLine (QuoteId, ProductId, Quantity, ListPrice, DiscountPct, NetPrice)
                OUTPUT INSERTED.Id
                VALUES (@quoteId, @productId, 3, 99.99, 10, 90);
                """,
                ("@quoteId", otherQuote),
                ("@productId", _productId));
        }

        public void SetStatus(string status) => Execute(_connection, _transaction, """
            UPDATE dbo.Quote SET [Status] = @status WHERE Id = @quoteId;
            """, ("@status", status), ("@quoteId", QuoteId));

        public string Status()
        {
            using var command = new SqlCommand(
                "SELECT [Status] FROM dbo.Quote WHERE Id = @quoteId;", _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);

            return (string)command.ExecuteScalar()!;
        }

        /// <summary>Rows updated, which is what the procedure reports instead of throwing.</summary>
        public int Update(
            int quantity, decimal discountPct,
            decimal? netPrice = null, int? lineId = null, int? siteId = null)
        {
            using var command = new SqlCommand("""
                EXEC dbo.spQuoteLine_Update
                    @Id = @lineId, @QuoteId = @quoteId, @SiteId = @siteId,
                    @Quantity = @quantity, @DiscountPct = @discount, @NetPrice = @netPrice;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@lineId", lineId ?? LineId);
            command.Parameters.AddWithValue("@quoteId", QuoteId);
            command.Parameters.AddWithValue("@siteId", siteId ?? SiteId);
            command.Parameters.AddWithValue("@quantity", quantity);
            command.Parameters.AddWithValue("@discount", discountPct);
            command.Parameters.AddWithValue("@netPrice", (object?)netPrice ?? DBNull.Value);

            return (int)command.ExecuteScalar()!;
        }

        /// <summary>Rows claimed: 1 when this call priced the quote, 0 when it was already decided.</summary>
        public int Price(int? siteId = null)
        {
            using var command = new SqlCommand(
                "EXEC dbo.spQuote_Price @QuoteId = @quoteId, @SiteId = @siteId;",
                _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);
            command.Parameters.AddWithValue("@siteId", siteId ?? SiteId);

            return (int)command.ExecuteScalar()!;
        }

        public sealed record LineRow(int Quantity, decimal DiscountPct, decimal NetPrice);

        public LineRow Line() => LineById(LineId);

        public LineRow LineById(int lineId)
        {
            using var command = new SqlCommand("""
                SELECT [Quantity], [DiscountPct], [NetPrice] FROM dbo.QuoteLine WHERE Id = @lineId;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@lineId", lineId);

            using var reader = command.ExecuteReader();

            reader.Read().Should().BeTrue("the fixture should have written a line");

            return new LineRow(reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2));
        }

        private static void Execute(
            SqlConnection connection, SqlTransaction transaction, string sql,
            params (string Name, object Value)[] parameters)
        {
            using var command = new SqlCommand(sql, connection, transaction);

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            command.ExecuteNonQuery();
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
