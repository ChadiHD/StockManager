using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// What happens when a quote becomes an order, and what happens when two callers try at once.
/// </summary>
/// <remarks>
/// Against a real database because every property here is a database property. The guard that
/// stops a double conversion is one atomic <c>UPDATE</c> whose <c>WHERE</c> and <c>SET</c> share
/// a row lock; who placed the order is a pair of foreign keys and a <c>CHECK</c>; one order per
/// quote is a filtered unique index. None of that can be evaluated with a substituted data
/// access layer — which is precisely how <c>Account.ApprovedBy</c> shipped broken in T3, with a
/// unit test asserting the defect.
///
/// **A procedure's <c>ROLLBACK TRANSACTION</c> rolls back this test's transaction too.** T-SQL
/// has no nested transactions: a bare <c>ROLLBACK</c> unwinds to the outermost <c>BEGIN</c>,
/// which here is <see cref="TestDatabase.OpenRollbackScope"/>'s. So a test that provokes the
/// procedure's <c>CATCH</c> must do it last and touch nothing afterwards. The validation
/// failures (50011, 50012) are raised before the transaction opens and are safe anywhere.
///
/// Skips without SMDATABASE_TEST_CONNECTION; see <see cref="TestDatabase"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class QuoteAcceptanceTests
{
    /// <summary>The number spOrder_ConvertFromQuote THROWs when the claim is refused.</summary>
    private const int QuoteAlreadyDecided = 50010;

    private const int NotExactlyOnePlacer = 50011;
    private const int ContactBelongsElsewhere = 50012;

    [SkippableFact]
    public void AStaffConversionRecordsTheStaffMemberAndNoContact()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = AcceptanceFixture.Create(connection, transaction);

            fixture.ConvertAsStaff(poNumber: "PO-99123");

            var order = fixture.Order();

            order.StaffId.Should().Be(fixture.StaffId);
            order.PlacedByContactId.Should().BeNull();
            order.PoNumber.Should().Be("PO-99123");
            order.Status.Should().Be("Awaiting payment");
            order.Reference.Should().StartWith("SO-");
        }
    }

    [SkippableFact]
    public void ACustomerAcceptanceRecordsTheContactAndNoStaffMember()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = AcceptanceFixture.Create(connection, transaction);

            fixture.ConvertAsCustomer(fixture.ContactId, poNumber: "PO-4471");

            var order = fixture.Order();

            // The whole reason StaffId had to become nullable: dbo.[User] holds staff, a
            // customer is a dbo.Contact, and an id-shaped string in StaffId fails
            // FK_Purchase_ToUser after the caller has been told it worked.
            order.StaffId.Should().BeNull();
            order.PlacedByContactId.Should().Be(fixture.ContactId);
            order.PoNumber.Should().Be("PO-4471");
        }
    }

    [SkippableTheory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AnOrderRecordsExactlyOnePlacer(bool withStaff, bool withContact)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = AcceptanceFixture.Create(connection, transaction);

            // Both and neither are the two states QuoteAcceptance's factory methods make
            // unreachable from C#. CK_Purchase_Placer is what holds the rule for anything that
            // reaches the procedure another way — a hand-run EXEC, or a caller yet to be
            // written — and this is the named error rather than the constraint violation,
            // because a constraint violation from inside a transaction reaches the API as a 500.
            var convert = () => fixture.Convert(
                staffId: withStaff ? fixture.StaffId : null,
                contactId: withContact ? fixture.ContactId : null,
                poNumber: null);

            convert.Should().Throw<SqlException>()
                .Which.Number.Should().Be(NotExactlyOnePlacer);

            fixture.OrderCount().Should().Be(0);
        }
    }

    [SkippableFact]
    public void AContactFromAnotherCompanyCannotPlaceThisAccountsOrder()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = AcceptanceFixture.Create(connection, transaction);
            var stranger = AcceptanceFixture.Create(connection, transaction);

            // FK_Purchase_ToContact is composite over (PlacedByContactId, AccountId) and would
            // refuse this too. The procedure says so first so the caller gets an answer rather
            // than a foreign-key failure out of the middle of a transaction.
            var convert = () => fixture.ConvertAsCustomer(stranger.ContactId);

            convert.Should().Throw<SqlException>()
                .Which.Number.Should().Be(ContactBelongsElsewhere);

            fixture.OrderCount().Should().Be(0);
        }
    }

    [SkippableFact]
    public void AnEmptyPoNumberIsStoredAsNothingRatherThanAsBlank()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = AcceptanceFixture.Create(connection, transaction);

            fixture.ConvertAsCustomer(fixture.ContactId, poNumber: "   ");

            // A customer who left the box alone has no PO number, and a document that quotes
            // one anyway prints an empty label. NULL is the answer "they did not give one".
            fixture.Order().PoNumber.Should().BeNull();
        }
    }

    [SkippableFact]
    public void ThePortalOrderStaysOutOfTheDesktopPosReport()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = AcceptanceFixture.Create(connection, transaction);

            fixture.ConvertAsStaff();

            // spPurchase_PurchaseReport had no Reference predicate at all until T5, so portal
            // orders were reported as POS sales by whichever admin converted them. The inner
            // join to dbo.[User] would now hide a customer-accepted order by accident; this is
            // the assertion that it is on purpose and covers the staff case too.
            fixture.AppearsInPosReport().Should().BeFalse();
        }
    }

    [SkippableFact]
    public void TheOrderIsReadableByTheQuoteItCameFrom()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = AcceptanceFixture.Create(connection, transaction);
            var other = AcceptanceFixture.Create(connection, transaction);

            fixture.ConvertAsStaff();

            // How OrderData reads back what it just created. Taking the store's newest order
            // instead would hand this caller the other store's, and under two conversions at
            // once it would do so without a second store existing.
            fixture.OrderReferenceByQuote(fixture.SiteId).Should().StartWith("SO-");
            fixture.OrderReferenceByQuote(other.SiteId).Should().BeNull(
                "a quote read for the wrong store has no order");
        }
    }

    [SkippableFact]
    public void ARejectedQuoteCannotBeConverted()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = AcceptanceFixture.Create(connection, transaction);

            fixture.SetStatus("Rejected");

            // Last statement in this scope on purpose: the procedure's CATCH rolls back, and
            // that rollback takes this test's transaction with it. See the remarks above.
            var convert = () => fixture.ConvertAsStaff();

            convert.Should().Throw<SqlException>()
                .Which.Number.Should().Be(QuoteAlreadyDecided);
        }
    }

    [SkippableFact]
    public void TheSecondConversionOfOneQuoteCreatesNothing()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var fixture = AcceptanceFixture.Create(connection, transaction);

            fixture.ConvertAsStaff();
            fixture.OrderCount().Should().Be(1);

            // The defect this guard exists for. Before T5 the UPDATE that marks a quote
            // Accepted carried no predicate on the current status, so this produced a second
            // order with its own SO- reference and its own copy of every line — reachable by
            // double-clicking Convert, and ordinary once a customer has an Accept button.
            //
            // Also last in its scope: the throw comes out of the procedure's CATCH.
            var again = () => fixture.ConvertAsCustomer(fixture.ContactId);

            again.Should().Throw<SqlException>()
                .Which.Number.Should().Be(QuoteAlreadyDecided);
        }
    }

    /// <summary>
    /// An approved account with a contact, a priced quote and a staff member, inside a
    /// transaction that is never committed.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="DelistedProductHistoryTests"/>'s fixture rather than shared:
    /// that one exists to prove a delisted product still resolves and carries the category
    /// mapping and staleness switches needed for the catalog reads, and this one needs a
    /// Contact and a Priced quote and no catalog at all.
    /// </remarks>
    private sealed class AcceptanceFixture
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly string _staffEmail;

        private AcceptanceFixture(
            SqlConnection connection, SqlTransaction transaction,
            int siteId, int accountId, int contactId, int quoteId,
            string staffId, string staffEmail)
        {
            _connection = connection;
            _transaction = transaction;
            _staffEmail = staffEmail;
            SiteId = siteId;
            AccountId = accountId;
            ContactId = contactId;
            QuoteId = quoteId;
            StaffId = staffId;
        }

        public int SiteId { get; }
        public int AccountId { get; }
        public int ContactId { get; }
        public int QuoteId { get; }
        public string StaffId { get; }

        public static AcceptanceFixture Create(SqlConnection connection, SqlTransaction transaction)
        {
            // SiteKey and Domain are unique, and a rolled-back transaction still collides with
            // a concurrent one that has not rolled back yet.
            var runId = Guid.NewGuid().ToString("N")[..12];

            var siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Acceptance test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);
                """,
                ("@key", $"accept-{runId}"),
                ("@domain", $"{runId}.accept.invalid"));

            var productId = Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                         QuantityInStock, Published, Delisted)
                OUTPUT INSERTED.Id
                VALUES (N'Acceptance fixture', N'Acceptance fixture.', 100, @sku, N'Acceptance',
                        5, 1, 0);
                """,
                ("@sku", $"ACC-{runId}"));

            var accountId = Scalar(connection, transaction, """
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                         PaymentTerms, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, N'Acceptance test customer', N'EUR', N'Credit',
                        N'Net 30', 10000, N'Approved', @siteId);
                """,
                ("@reference", $"AC-A{runId[..6]}"),
                ("@siteId", siteId));

            var contactId = Scalar(connection, transaction, """
                INSERT INTO dbo.Contact (AccountId, FirstName, LastName, Email, RoleInAccount,
                                         IsPrimary, [Status])
                OUTPUT INSERTED.Id
                VALUES (@accountId, N'Acceptance', N'Buyer', @email, N'Buyer', 1, N'Active');
                """,
                ("@accountId", accountId),
                ("@email", $"buyer-{runId}@accept.invalid"));

            var quoteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, [Status], ExpiresDate, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, @accountId, N'EUR', N'Priced', @expires, @siteId);
                """,
                ("@reference", $"QT-A{runId[..6]}"),
                ("@accountId", accountId),
                ("@expires", DateTime.UtcNow.AddDays(14)),
                ("@siteId", siteId));

            Execute(connection, transaction, """
                INSERT INTO dbo.QuoteLine (QuoteId, ProductId, Quantity, ListPrice, DiscountPct, NetPrice)
                VALUES (@quoteId, @productId, 3, 100, 10, 90);
                """,
                ("@quoteId", quoteId),
                ("@productId", productId));

            // Purchase.StaffId is a foreign key into dbo.User(UserId), so the staff path needs
            // a real staff row.
            var staffId = $"staff-{runId}";
            var staffEmail = $"{runId}@accept.invalid";

            Execute(connection, transaction, """
                INSERT INTO dbo.[User] (UserId, FirstName, LastName, EmailAddress)
                VALUES (@staffId, N'Acceptance', N'Fixture', @email);
                """,
                ("@staffId", staffId),
                ("@email", staffEmail));

            return new AcceptanceFixture(
                connection, transaction, siteId, accountId, contactId, quoteId, staffId, staffEmail);
        }

        public void SetStatus(string status) => Execute(_connection, _transaction, """
            UPDATE dbo.Quote SET [Status] = @status WHERE Id = @quoteId;
            """, ("@status", status), ("@quoteId", QuoteId));

        public void ConvertAsStaff(string? poNumber = null) =>
            Convert(StaffId, null, poNumber);

        public void ConvertAsCustomer(int contactId, string? poNumber = null) =>
            Convert(null, contactId, poNumber);

        public void Convert(string? staffId, int? contactId, string? poNumber)
        {
            using var command = new SqlCommand("""
                DECLARE @OrderId int, @OrderRef nvarchar(20);

                EXEC dbo.spOrder_ConvertFromQuote
                    @QuoteId = @quoteId,
                    @Id = @OrderId OUTPUT, @Reference = @OrderRef OUTPUT,
                    @SiteId = @siteId,
                    @StaffId = @staffId,
                    @PlacedByContactId = @contactId,
                    @PoNumber = @poNumber;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);
            command.Parameters.AddWithValue("@siteId", SiteId);
            command.Parameters.AddWithValue("@staffId", (object?)staffId ?? DBNull.Value);
            command.Parameters.AddWithValue("@contactId", (object?)contactId ?? DBNull.Value);
            command.Parameters.AddWithValue("@poNumber", (object?)poNumber ?? DBNull.Value);

            command.ExecuteNonQuery();
        }

        public sealed record OrderRow(
            string Reference, string? StaffId, int? PlacedByContactId, string? PoNumber, string? Status);

        public OrderRow Order()
        {
            using var command = new SqlCommand("""
                SELECT [Reference], [StaffId], [PlacedByContactId], [PoNumber], [Status]
                FROM dbo.Purchase
                WHERE [QuoteId] = @quoteId;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);

            using var reader = command.ExecuteReader();

            reader.Read().Should().BeTrue("the conversion should have written an order");

            return new OrderRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }

        public int OrderCount() => Scalar(_connection, _transaction, """
            SELECT COUNT(*) FROM dbo.Purchase WHERE [QuoteId] = @quoteId;
            """, ("@quoteId", QuoteId));

        /// <summary>The reference spOrder_GetByQuote returns, or null when it returns nothing.</summary>
        public string? OrderReferenceByQuote(int siteId)
        {
            using var command = new SqlCommand(
                "EXEC dbo.spOrder_GetByQuote @QuoteId = @quoteId, @SiteId = @siteId;",
                _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);
            command.Parameters.AddWithValue("@siteId", siteId);

            using var reader = command.ExecuteReader();

            return reader.Read() ? reader.GetString(reader.GetOrdinal("Reference")) : null;
        }

        /// <summary>Whether this fixture's staff member shows up in the POS report.</summary>
        public bool AppearsInPosReport()
        {
            using var command = new SqlCommand(
                "EXEC dbo.spPurchase_PurchaseReport;", _connection, _transaction);

            using var reader = command.ExecuteReader();

            var email = reader.GetOrdinal("EmailAddress");

            while (reader.Read())
            {
                if (string.Equals(reader.GetString(email), _staffEmail, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
