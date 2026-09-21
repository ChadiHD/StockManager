using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// That one customer cannot read another customer's quote or order by guessing a reference.
/// </summary>
/// <remarks>
/// **A reference is not an authorisation.** They come from <c>dbo.QuoteReferenceSequence</c> and
/// <c>dbo.OrderReferenceSequence</c> and read QT-0041 and SO-0012, so every customer-facing read
/// carries the account in its predicate as well as the site. Scoping by site alone is right for
/// an admin — who may see every quote in their store — and would let any signed-in customer read
/// any other customer's lines and prices by changing a digit.
///
/// This is the class that says the predicate is there. It has no failure mode that looks like an
/// error: leave the account out and every test in the storefront still passes, and the only
/// symptom is that the wrong person can read a price. A substituted data layer cannot tell you
/// which rows a <c>WHERE</c> clause returns.
///
/// Skips without SMDATABASE_TEST_CONNECTION; see <see cref="TestDatabase"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class CustomerDocumentScopeTests
{
    private const int RejectionNeedsAReason = 50032;

    [SkippableFact]
    public void AQuoteListShowsOneAccountsQuotesAndNobodyElses()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = DocumentScopeScenario.Create(connection, transaction);

            var mine = store.Quote(store.AccountId, "Priced");
            var theirs = store.Quote(store.OtherAccountId, "Priced");

            store.QuotesFor(store.AccountId).Should().Equal(mine);
            store.QuotesFor(store.OtherAccountId).Should().Equal(theirs);
        }
    }

    [SkippableFact]
    public void AnotherAccountsQuoteReadsAsNotFoundRatherThanForbidden()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = DocumentScopeScenario.Create(connection, transaction);

            var theirs = store.Quote(store.OtherAccountId, "Priced");

            // Empty, and empty is also what a reference that names nothing returns. With
            // sequential references a distinguishable refusal would confirm which ones exist,
            // which is why spAccountDocument_GetById answers 404 rather than 403 too.
            store.QuoteFor(theirs, store.AccountId).Should().BeNull();
            store.QuoteFor("QT-NOPE", store.AccountId).Should().BeNull();
            store.QuoteFor(theirs, store.OtherAccountId).Should().Be(theirs);
        }
    }

    [SkippableFact]
    public void AnotherAccountsQuoteLinesAreNotReadableByQuoteId()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = DocumentScopeScenario.Create(connection, transaction);

            var theirs = store.Quote(store.OtherAccountId, "Priced");
            var theirQuoteId = store.QuoteId(theirs);

            // The predicate is repeated on the line read rather than left to the caller having
            // resolved the quote first. Defence that depends on call order survives exactly
            // until somebody adds a second caller.
            store.QuoteLinesFor(theirQuoteId, store.AccountId).Should().BeEmpty();
            store.QuoteLinesFor(theirQuoteId, store.OtherAccountId).Should().ContainSingle();
        }
    }

    [SkippableFact]
    public void AnotherAccountsOrderAndItsLinesAreBothOutOfReach()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = DocumentScopeScenario.Create(connection, transaction);

            var theirQuote = store.Quote(store.OtherAccountId, "Priced");
            var theirOrder = store.Convert(theirQuote);
            var theirOrderId = store.OrderId(theirOrder);

            store.OrdersFor(store.AccountId).Should().BeEmpty();
            store.OrderFor(theirOrder, store.AccountId).Should().BeNull();
            store.OrderLinesFor(theirOrderId, store.AccountId).Should().BeEmpty();

            store.OrderFor(theirOrder, store.OtherAccountId).Should().Be(theirOrder);
            store.OrderLinesFor(theirOrderId, store.OtherAccountId).Should().ContainSingle();
        }
    }

    [SkippableFact]
    public void APosSaleNeverAppearsAmongACustomersOrders()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = DocumentScopeScenario.Create(connection, transaction);

            var quote = store.Quote(store.AccountId, "Priced");
            var order = store.Convert(quote);

            store.AddPosSale();

            // dbo.Purchase does double duty, and the Reference predicate is what keeps the
            // halves apart. A POS receipt has no account, so the account filter would hide it
            // as well — which is exactly why both predicates are stated rather than one relied
            // on to imply the other.
            store.OrdersFor(store.AccountId).Should().Equal(order);
        }
    }

    [SkippableFact]
    public void ARejectionRecordsTheCustomersReasonAndOnlyOnce()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = DocumentScopeScenario.Create(connection, transaction);

            var quote = store.Quote(store.AccountId, "Priced");

            store.Reject(quote, store.AccountId, "Lead time too long").Should().Be(1);

            var rejected = store.QuoteRow(quote);

            rejected.Status.Should().Be("Rejected");
            rejected.RejectedReason.Should().Be("Lead time too long");

            // A claim, like the accept: a rejection racing another decision cannot both
            // succeed, and a rowcount of zero means somebody else got there first.
            store.Reject(quote, store.AccountId, "Changed my mind").Should().Be(0);
            store.QuoteRow(quote).RejectedReason.Should().Be("Lead time too long");
        }
    }

    [SkippableFact]
    public void AnotherAccountCannotRejectThisAccountsQuote()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = DocumentScopeScenario.Create(connection, transaction);

            var mine = store.Quote(store.AccountId, "Priced");

            store.Reject(mine, store.OtherAccountId, "Not mine to reject").Should().Be(0);
            store.QuoteRow(mine).Status.Should().Be("Priced");
        }
    }

    [SkippableTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ARejectionWithoutAReasonIsRefused(string? reason)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = DocumentScopeScenario.Create(connection, transaction);

            var quote = store.Quote(store.AccountId, "Priced");

            // Required by the procedure, not only by the page. "They said no" is not an answer
            // to anybody asking what went wrong with the price, and the same rule governs
            // spAccount_Reject in the other direction.
            var reject = () => store.Reject(quote, store.AccountId, reason);

            reject.Should().Throw<SqlException>()
                .Which.Number.Should().Be(RejectionNeedsAReason);
        }
    }

    /// <summary>Two accounts in one store, each able to have quotes and orders.</summary>
    private sealed class DocumentScopeScenario
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly string _runId;
        private readonly int _productId;
        private readonly string _staffId;

        private DocumentScopeScenario(
            SqlConnection connection, SqlTransaction transaction, string runId,
            int siteId, int accountId, int otherAccountId, int productId, string staffId)
        {
            _connection = connection;
            _transaction = transaction;
            _runId = runId;
            _productId = productId;
            _staffId = staffId;
            SiteId = siteId;
            AccountId = accountId;
            OtherAccountId = otherAccountId;
        }

        public int SiteId { get; }
        public int AccountId { get; }
        public int OtherAccountId { get; }

        public static DocumentScopeScenario Create(SqlConnection connection, SqlTransaction transaction)
        {
            var runId = Guid.NewGuid().ToString("N")[..12];

            var siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Scope test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);
                """,
                ("@key", $"scope-{runId}"),
                ("@domain", $"{runId}.scope.invalid"));

            var productId = Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                         QuantityInStock, Published, Delisted)
                OUTPUT INSERTED.Id
                VALUES (N'Scope fixture', N'Scope fixture.', 100, @sku, N'Scope', 5, 1, 0);
                """,
                ("@sku", $"SCP-{runId}"));

            var accountId = Account(connection, transaction, siteId, runId, "A");
            var otherAccountId = Account(connection, transaction, siteId, runId, "B");

            // Purchase.StaffId is a foreign key into dbo.User(UserId), so converting needs a
            // real staff row — the shape of trap the E2E suite found in T3.
            var staffId = $"staff-{runId}";

            Execute(connection, transaction, """
                INSERT INTO dbo.[User] (UserId, FirstName, LastName, EmailAddress)
                VALUES (@staffId, N'Scope', N'Fixture', @email);
                """,
                ("@staffId", staffId),
                ("@email", $"{runId}@scope.invalid"));

            return new DocumentScopeScenario(
                connection, transaction, runId, siteId, accountId, otherAccountId,
                productId, staffId);
        }

        private static int Account(
            SqlConnection connection, SqlTransaction transaction,
            int siteId, string runId, string suffix) =>
            Scalar(connection, transaction, """
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                         PaymentTerms, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, @company, N'EUR', N'Credit', N'Net 30', 10000,
                        N'Approved', @siteId);
                """,
                ("@reference", $"AC-{suffix}{runId[..6]}"),
                ("@company", $"Scope customer {suffix}"),
                ("@siteId", siteId));

        /// <summary>A quote for one account, with one line, in the given status.</summary>
        public string Quote(int accountId, string status)
        {
            var reference = $"QT-{_runId[..4]}{Guid.NewGuid().ToString("N")[..6]}";

            var quoteId = Scalar(_connection, _transaction, """
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, [Status], ExpiresDate, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, @accountId, N'EUR', @status, @expires, @siteId);
                """,
                ("@reference", reference),
                ("@accountId", accountId),
                ("@status", status),
                ("@expires", DateTime.UtcNow.AddDays(14)),
                ("@siteId", SiteId));

            Execute(_connection, _transaction, """
                INSERT INTO dbo.QuoteLine (QuoteId, ProductId, Quantity, ListPrice, DiscountPct, NetPrice)
                VALUES (@quoteId, @productId, 2, 100, 0, 100);
                """,
                ("@quoteId", quoteId),
                ("@productId", _productId));

            return reference;
        }

        /// <summary>Converts a quote and returns the order's reference.</summary>
        public string Convert(string quoteReference)
        {
            using var command = new SqlCommand("""
                DECLARE @OrderId int, @OrderRef nvarchar(20);
                DECLARE @QuoteId int = (SELECT [Id] FROM dbo.Quote WHERE [Reference] = @reference);

                EXEC dbo.spOrder_ConvertFromQuote
                    @QuoteId = @QuoteId,
                    @Id = @OrderId OUTPUT, @Reference = @OrderRef OUTPUT,
                    @SiteId = @siteId, @StaffId = @staffId;

                SELECT @OrderRef;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@reference", quoteReference);
            command.Parameters.AddWithValue("@siteId", SiteId);
            command.Parameters.AddWithValue("@staffId", _staffId);

            return (string)command.ExecuteScalar()!;
        }

        /// <summary>A desktop POS sale: no reference, no account, no site.</summary>
        public void AddPosSale() => Execute(_connection, _transaction, """
            INSERT INTO dbo.Purchase (StaffId, PurchaseDate, SubTotal, VAT, FinalPrice)
            VALUES (@staffId, SYSUTCDATETIME(), 10, 0, 10);
            """, ("@staffId", _staffId));

        public List<string> QuotesFor(int accountId) =>
            References("EXEC dbo.spQuote_GetByAccount @AccountId = @accountId, @SiteId = @siteId;",
                ("@accountId", accountId), ("@siteId", SiteId));

        public string? QuoteFor(string reference, int accountId) =>
            References("""
                EXEC dbo.spQuote_GetForAccount
                    @Reference = @reference, @AccountId = @accountId, @SiteId = @siteId;
                """,
                ("@reference", reference), ("@accountId", accountId), ("@siteId", SiteId))
                .FirstOrDefault();

        public List<string> OrdersFor(int accountId) =>
            References("EXEC dbo.spOrder_GetByAccount @AccountId = @accountId, @SiteId = @siteId;",
                ("@accountId", accountId), ("@siteId", SiteId));

        public string? OrderFor(string reference, int accountId) =>
            References("""
                EXEC dbo.spOrder_GetForAccount
                    @Reference = @reference, @AccountId = @accountId, @SiteId = @siteId;
                """,
                ("@reference", reference), ("@accountId", accountId), ("@siteId", SiteId))
                .FirstOrDefault();

        public List<string> QuoteLinesFor(int quoteId, int accountId) =>
            Skus("""
                EXEC dbo.spQuoteLine_GetForAccount
                    @QuoteId = @quoteId, @AccountId = @accountId, @SiteId = @siteId;
                """,
                ("@quoteId", quoteId), ("@accountId", accountId), ("@siteId", SiteId));

        public List<string> OrderLinesFor(int purchaseId, int accountId) =>
            Skus("""
                EXEC dbo.spOrderLine_GetForAccount
                    @PurchaseId = @purchaseId, @AccountId = @accountId, @SiteId = @siteId;
                """,
                ("@purchaseId", purchaseId), ("@accountId", accountId), ("@siteId", SiteId));

        public int Reject(string reference, int accountId, string? reason)
        {
            using var command = new SqlCommand("""
                DECLARE @QuoteId int = (SELECT [Id] FROM dbo.Quote WHERE [Reference] = @reference);

                EXEC dbo.spQuote_Reject
                    @QuoteId = @QuoteId, @AccountId = @accountId,
                    @SiteId = @siteId, @Reason = @reason;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@reference", reference);
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@siteId", SiteId);
            command.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);

            return (int)command.ExecuteScalar()!;
        }

        public sealed record QuoteStateRow(string Status, string? RejectedReason);

        public QuoteStateRow QuoteRow(string reference)
        {
            using var command = new SqlCommand("""
                SELECT [Status], [RejectedReason] FROM dbo.Quote WHERE [Reference] = @reference;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@reference", reference);

            using var reader = command.ExecuteReader();

            reader.Read().Should().BeTrue();

            return new QuoteStateRow(
                reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
        }

        public int QuoteId(string reference) => Scalar(_connection, _transaction,
            "SELECT [Id] FROM dbo.Quote WHERE [Reference] = @reference;",
            ("@reference", reference));

        public int OrderId(string reference) => Scalar(_connection, _transaction,
            "SELECT [Id] FROM dbo.Purchase WHERE [Reference] = @reference;",
            ("@reference", reference));

        private List<string> References(string sql, params (string Name, object Value)[] parameters) =>
            Column(sql, "Reference", parameters);

        private List<string> Skus(string sql, params (string Name, object Value)[] parameters) =>
            Column(sql, "Sku", parameters);

        private List<string> Column(
            string sql, string column, (string Name, object Value)[] parameters)
        {
            using var command = new SqlCommand(sql, _connection, _transaction);

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            using var reader = command.ExecuteReader();

            var values = new List<string>();
            var ordinal = -1;

            while (reader.Read())
            {
                if (ordinal < 0)
                {
                    ordinal = reader.GetOrdinal(column);
                }

                values.Add(reader.GetString(ordinal));
            }

            return values;
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
