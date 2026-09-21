using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// What the credit limit refuses, on which path, and what it records either way.
/// </summary>
/// <remarks>
/// **This compares one order against the limit, not an outstanding balance.** A balance needs
/// a settled state, and <c>dbo.Purchase</c> has none: its statuses are Awaiting payment,
/// Processing, Fulfilled and Cancelled, and Fulfilled means shipped and invoiced — on credit
/// terms, precisely when the money is not yet in. Summing unsettled orders would count every
/// order a customer ever placed and block them for good. The check is named for what it does,
/// and the balance arrives with invoicing.
///
/// A database test because the check lives inside the acceptance transaction, which is where
/// it has to be: two buyers at one company accepting at once would each pass a check made
/// before it.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class CreditLimitTests
{
    /// <summary>The number spOrder_ConvertFromQuote THROWs when the order is over the limit.</summary>
    private const int CreditLimitExceeded = 50040;

    [SkippableFact]
    public void ACustomerIsRefusedAnOrderLargerThanTheirLimit()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            // 5 x 100 against a 300 limit on thirty-day terms.
            var fixture = CreditFixture.Create(connection, transaction, creditLimit: 300m, termsDays: 30);

            // Last in the scope and nothing after it: the procedure's CATCH issues a bare
            // ROLLBACK, which in T-SQL unwinds to this test's own BEGIN. QuoteAcceptanceTests
            // documents the same trap.
            var convert = () => fixture.Convert(enforce: true);

            convert.Should().Throw<SqlException>()
                .Which.Number.Should().Be(CreditLimitExceeded);
        }
    }

    [SkippableFact]
    public void AnAdminIsNotRefused_ButTheOrderRecordsThatItWentOver()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = CreditFixture.Create(connection, transaction, creditLimit: 300m, termsDays: 30);

            // Converting for a customer who rang up may deliberately exceed the limit. That
            // is a commercial decision with somebody's name on it, and the flag is how it
            // stays visible afterwards rather than a refusal nobody can override.
            fixture.Convert(enforce: false);

            fixture.Order().CreditLimitExceeded.Should().BeTrue();
        }
    }

    [SkippableFact]
    public void AnOrderInsideTheLimitIsUnremarkable()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = CreditFixture.Create(connection, transaction, creditLimit: 5000m, termsDays: 30);

            fixture.Convert(enforce: true);

            fixture.Order().CreditLimitExceeded.Should().BeFalse();
        }
    }

    [SkippableFact]
    public void APrepaidAccountHasNoLimitToExceed()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            // Zero days and a zero limit, which is every account the register creates.
            // Reading that as "a limit of nothing" would refuse every order a prepaid
            // customer ever placed.
            var fixture = CreditFixture.Create(connection, transaction, creditLimit: 0m, termsDays: 0);

            fixture.Convert(enforce: true);

            var order = fixture.Order();

            order.CreditLimitExceeded.Should().BeFalse();
            // Due the day it was raised, which is what prepaid means.
            order.DueDate.Should().Be(DateTime.UtcNow.Date);
        }
    }

    [SkippableFact]
    public void CreditTermsPutTheDueDateTheirNumberOfDaysOut()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = CreditFixture.Create(connection, transaction, creditLimit: 5000m, termsDays: 45);

            fixture.Convert(enforce: true);

            // Snapshotted, because an account's terms can be renegotiated and an invoice
            // already sent must not move.
            fixture.Order().DueDate.Should().Be(DateTime.UtcNow.Date.AddDays(45));
        }
    }

    private sealed class CreditFixture
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;

        private CreditFixture(
            SqlConnection connection, SqlTransaction transaction,
            int siteId, int quoteId, int contactId)
        {
            _connection = connection;
            _transaction = transaction;
            SiteId = siteId;
            QuoteId = quoteId;
            ContactId = contactId;
        }

        public int SiteId { get; }
        public int QuoteId { get; }
        public int ContactId { get; }

        public static CreditFixture Create(
            SqlConnection connection, SqlTransaction transaction,
            decimal creditLimit, int termsDays)
        {
            var runId = Guid.NewGuid().ToString("N")[..12];

            int siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Credit test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);
                """,
                ("@key", $"credit-{runId}"),
                ("@domain", $"{runId}.credit.invalid"));

            int productId = Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                         QuantityInStock, Published, Delisted, IsTaxable)
                OUTPUT INSERTED.Id
                VALUES (N'Credit fixture', N'Credit fixture.', 100, @sku, N'Credit', 50, 1, 0, 0);
                """, ("@sku", $"CRD-{runId}"));

            int accountId = Scalar(connection, transaction, """
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                         PaymentTerms, PaymentTermsDays, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, N'Credit test customer', N'EUR', @method,
                        @terms, @days, @limit, N'Approved', @siteId);
                """,
                ("@reference", $"AC-C{runId[..6]}"),
                ("@method", termsDays > 0 ? "Credit" : "Card"),
                ("@terms", termsDays > 0 ? $"Net {termsDays}" : "Prepaid"),
                ("@days", termsDays),
                ("@limit", creditLimit),
                ("@siteId", siteId));

            int contactId = Scalar(connection, transaction, """
                INSERT INTO dbo.Contact (AccountId, FirstName, LastName, Email, RoleInAccount,
                                         IsPrimary, [Status])
                OUTPUT INSERTED.Id
                VALUES (@accountId, N'Credit', N'Buyer', @email, N'Buyer', 1, N'Active');
                """,
                ("@accountId", accountId),
                ("@email", $"buyer-{runId}@credit.invalid"));

            int quoteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, [Status], SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, @accountId, N'EUR', N'Priced', @siteId);
                """,
                ("@reference", $"QT-C{runId[..6]}"),
                ("@accountId", accountId),
                ("@siteId", siteId));

            // 5 x 100 = 500, so a 300 limit is exceeded and a 5000 one is not.
            Execute(connection, transaction, """
                INSERT INTO dbo.QuoteLine (QuoteId, ProductId, Quantity, ListPrice, DiscountPct, NetPrice)
                VALUES (@quoteId, @productId, 5, 100, 0, 100);
                """, ("@quoteId", quoteId), ("@productId", productId));

            return new CreditFixture(connection, transaction, siteId, quoteId, contactId);
        }

        public void Convert(bool enforce)
        {
            using var command = new SqlCommand("""
                DECLARE @OrderId int, @OrderRef nvarchar(20);

                EXEC dbo.spOrder_ConvertFromQuote
                    @QuoteId = @quoteId,
                    @Id = @OrderId OUTPUT, @Reference = @OrderRef OUTPUT,
                    @SiteId = @siteId,
                    @PlacedByContactId = @contactId,
                    @EnforceCreditLimit = @enforce;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);
            command.Parameters.AddWithValue("@siteId", SiteId);
            command.Parameters.AddWithValue("@contactId", ContactId);
            command.Parameters.AddWithValue("@enforce", enforce);

            command.ExecuteNonQuery();
        }

        public sealed record OrderRow(bool CreditLimitExceeded, DateTime? DueDate);

        public OrderRow Order()
        {
            using var command = new SqlCommand("""
                SELECT [CreditLimitExceeded], [DueDate]
                FROM dbo.Purchase WHERE [QuoteId] = @quoteId;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);

            using var reader = command.ExecuteReader();

            reader.Read().Should().BeTrue("the conversion should have written an order");

            return new OrderRow(
                reader.GetBoolean(0),
                reader.IsDBNull(1) ? null : reader.GetDateTime(1));
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
