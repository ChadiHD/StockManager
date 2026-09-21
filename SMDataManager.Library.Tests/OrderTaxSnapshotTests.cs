using FluentAssertions;
using Microsoft.Data.SqlClient;
using SMDataManager.Library.Tax;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// What an acceptance writes down about tax, and what it charges per line.
/// </summary>
/// <remarks>
/// The decision is C#, and <c>TaxRuleSetTests</c> drives the whole table with no database.
/// What has to be evaluated here is the half the database owns: the rate reaches a line only
/// if <c>Product.IsTaxable</c> says so, the order's VAT is the sum of its lines rather than a
/// separately rounded total, and the treatment and legend are on the row afterwards.
///
/// A mixed order is the case worth building a fixture for. One taxable product and one exempt
/// one on the same acceptance is where a per-order rate would give the wrong answer, and it is
/// exactly the shape that looks right in every single-product test.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class OrderTaxSnapshotTests
{
    [SkippableFact]
    public void AMixedOrderTaxesOnlyTheTaxableLine()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = TaxFixture.Create(connection, transaction);

            // 2 x 100 taxable at 23% is 46.00; 3 x 50 exempt is nothing.
            fixture.Convert(new TaxAssessment(TaxTreatment.DomesticStandard, 23m, "VAT at 23%."));

            var lines = fixture.Lines();

            lines.Should().HaveCount(2);
            lines.Single(line => line.Taxable).Vat.Should().Be(46.00m);
            lines.Single(line => line.Taxable).RatePct.Should().Be(23m);

            // The exempt line carries a zero rate as well as zero tax, so a document showing
            // the rate beside the money does not claim it was taxed at 23% and rounded away.
            lines.Single(line => !line.Taxable).Vat.Should().Be(0m);
            lines.Single(line => !line.Taxable).RatePct.Should().Be(0m);
        }
    }

    [SkippableFact]
    public void TheOrdersVatIsTheSumOfItsOwnLines()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = TaxFixture.Create(connection, transaction);

            fixture.Convert(new TaxAssessment(TaxTreatment.DomesticStandard, 23m, "VAT at 23%."));

            var order = fixture.Order();
            var lineVat = fixture.Lines().Sum(line => line.Vat);

            // Summed rather than re-derived from the order total: rounding the total
            // separately gives an order whose VAT does not equal the figures printed under it.
            order.Vat.Should().Be(lineVat);
            order.SubTotal.Should().Be(350m);
            order.FinalPrice.Should().Be(order.SubTotal + order.Vat);
        }
    }

    [SkippableFact]
    public void TheTreatmentAndItsLegendAreOnTheOrderAfterwards()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = TaxFixture.Create(connection, transaction);

            fixture.Convert(new TaxAssessment(
                TaxTreatment.IntraEuReverseCharge, 0m, "VAT reverse charge: Article 196."));

            var order = fixture.Order();

            // Snapshotted, not re-derived. The rate can change and the rule set can be
            // corrected; the document a customer filed must not quietly change with them.
            order.TaxTreatment.Should().Be(TaxTreatment.IntraEuReverseCharge);
            order.TaxLegend.Should().Contain("Article 196");
            order.Vat.Should().Be(0m);
            order.FinalPrice.Should().Be(order.SubTotal);
        }
    }

    [SkippableFact]
    public void AnUnassessedConversionRaisesAnUntaxedOrderRatherThanFailing()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = TaxFixture.Create(connection, transaction);

            // The parameters are defaulted, so a caller that predates T6 still works. That is
            // the behaviour every order had until now, and it is visible on the document
            // rather than hidden behind an error — the right direction for a default to be
            // wrong in.
            fixture.Convert(assessment: null);

            var order = fixture.Order();

            order.Vat.Should().Be(0m);
            order.TaxTreatment.Should().BeNull();
        }
    }

    private sealed class TaxFixture
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;

        private TaxFixture(
            SqlConnection connection, SqlTransaction transaction,
            int siteId, int quoteId, int taxableProductId)
        {
            _connection = connection;
            _transaction = transaction;
            SiteId = siteId;
            QuoteId = quoteId;
            TaxableProductId = taxableProductId;
        }

        public int SiteId { get; }
        public int QuoteId { get; }
        public int TaxableProductId { get; }

        public static TaxFixture Create(SqlConnection connection, SqlTransaction transaction)
        {
            var runId = Guid.NewGuid().ToString("N")[..12];

            int siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive,
                                      TaxRuleSet, StandardTaxRatePct)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Tax test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1, N'eu-b2b', 23);
                """,
                ("@key", $"tax-{runId}"),
                ("@domain", $"{runId}.tax.invalid"));

            int taxable = Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                         QuantityInStock, Published, Delisted, IsTaxable)
                OUTPUT INSERTED.Id
                VALUES (N'Taxable', N'Taxable.', 100, @sku, N'Tax', 5, 1, 0, 1);
                """, ("@sku", $"TAXY-{runId}"));

            int exempt = Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                         QuantityInStock, Published, Delisted, IsTaxable)
                OUTPUT INSERTED.Id
                VALUES (N'Exempt', N'Exempt.', 50, @sku, N'Tax', 5, 1, 0, 0);
                """, ("@sku", $"EXMT-{runId}"));

            int accountId = Scalar(connection, transaction, """
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                         PaymentTerms, PaymentTermsDays, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, N'Tax test customer', N'EUR', N'Card',
                        N'Prepaid', 0, 0, N'Approved', @siteId);
                """,
                ("@reference", $"AC-X{runId[..6]}"),
                ("@siteId", siteId));

            int contactId = Scalar(connection, transaction, """
                INSERT INTO dbo.Contact (AccountId, FirstName, LastName, Email, RoleInAccount,
                                         IsPrimary, [Status])
                OUTPUT INSERTED.Id
                VALUES (@accountId, N'Tax', N'Buyer', @email, N'Buyer', 1, N'Active');
                """,
                ("@accountId", accountId),
                ("@email", $"buyer-{runId}@tax.invalid"));

            int quoteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, [Status], SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, @accountId, N'EUR', N'Priced', @siteId);
                """,
                ("@reference", $"QT-X{runId[..6]}"),
                ("@accountId", accountId),
                ("@siteId", siteId));

            Execute(connection, transaction, """
                INSERT INTO dbo.QuoteLine (QuoteId, ProductId, Quantity, ListPrice, DiscountPct, NetPrice)
                VALUES (@quoteId, @taxable, 2, 100, 0, 100),
                       (@quoteId, @exempt, 3, 50, 0, 50);
                """,
                ("@quoteId", quoteId), ("@taxable", taxable), ("@exempt", exempt));

            return new TaxFixture(connection, transaction, siteId, quoteId, taxable)
            {
                ContactId = contactId
            };
        }

        public int ContactId { get; private init; }

        public void Convert(TaxAssessment? assessment)
        {
            using var command = new SqlCommand("""
                DECLARE @OrderId int, @OrderRef nvarchar(20);

                EXEC dbo.spOrder_ConvertFromQuote
                    @QuoteId = @quoteId,
                    @Id = @OrderId OUTPUT, @Reference = @OrderRef OUTPUT,
                    @SiteId = @siteId,
                    @PlacedByContactId = @contactId,
                    @TaxTreatment = @treatment,
                    @TaxLegend = @legend,
                    @TaxRatePct = @rate;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);
            command.Parameters.AddWithValue("@siteId", SiteId);
            command.Parameters.AddWithValue("@contactId", ContactId);
            command.Parameters.AddWithValue("@treatment", (object?)assessment?.Treatment ?? DBNull.Value);
            command.Parameters.AddWithValue("@legend", (object?)assessment?.Legend ?? DBNull.Value);
            command.Parameters.AddWithValue("@rate", assessment?.RatePct ?? 0m);

            command.ExecuteNonQuery();
        }

        public sealed record OrderRow(
            decimal SubTotal, decimal Vat, decimal FinalPrice, string? TaxTreatment, string? TaxLegend);

        public OrderRow Order()
        {
            using var command = new SqlCommand("""
                SELECT [SubTotal], [VAT], [FinalPrice], [TaxTreatment], [TaxLegend]
                FROM dbo.Purchase WHERE [QuoteId] = @quoteId;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);

            using var reader = command.ExecuteReader();

            reader.Read().Should().BeTrue("the conversion should have written an order");

            return new OrderRow(
                reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }

        public sealed record LineRow(bool Taxable, decimal Vat, decimal RatePct);

        public List<LineRow> Lines()
        {
            using var command = new SqlCommand("""
                SELECT [d].[ProductId], [d].[VAT], [d].[TaxRatePct]
                FROM dbo.PurchaseDetail d
                INNER JOIN dbo.Purchase p ON p.[Id] = d.[PurchaseId]
                WHERE p.[QuoteId] = @quoteId;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);

            using var reader = command.ExecuteReader();

            var lines = new List<LineRow>();

            while (reader.Read())
            {
                lines.Add(new LineRow(
                    reader.GetInt32(0) == TaxableProductId,
                    reader.GetDecimal(1),
                    reader.GetDecimal(2)));
            }

            return lines;
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
