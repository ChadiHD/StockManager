using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SMDataManager.Library.Email;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// The outbox's claim, lease and bookkeeping, and the procedures that queue into it.
/// </summary>
/// <remarks>
/// A database test because the guard is one T-SQL statement: two dispatchers must not both
/// come away holding a row, and that is a property of the UPDATE's locking, not of any C#.
///
/// The claim is not scoped to a site — the dispatcher drains every store — so a development
/// database with rows of its own would hand some of them to these tests too. Every assertion
/// therefore filters to the rows the test wrote, and the batch is large enough that a test's
/// own rows are never crowded out. Everything rolls back, including the claims.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class EmailOutboxTests
{
    private const int NoAddress = 50060;

    [SkippableFact]
    public void ADueMessageIsClaimedByOneDispatcherAndNotTheNext()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var id = db.Enqueue();

            var first = db.Claim(Guid.NewGuid()).Single(row => row.Id == id);
            first.Status.Should().Be("Sending");
            first.Attempts.Should().Be(1, "an attempt is counted when it is claimed");

            db.Claim(Guid.NewGuid()).Should().NotContain(row => row.Id == id);
        }
    }

    [SkippableFact]
    public void AMessageWaitingOutItsBackoffIsLeftAlone()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var id = db.Enqueue();
            db.Execute("UPDATE dbo.EmailOutbox SET NextAttemptUtc = DATEADD(MINUTE, 5, SYSUTCDATETIME()) WHERE Id = @id",
                ("@id", id));

            db.Claim(Guid.NewGuid()).Should().NotContain(row => row.Id == id);
        }
    }

    [SkippableFact]
    public void AnExpiredLeaseIsTakenOver_AndTheLateDispatcherCannotRecordOverIt()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var id = db.Enqueue();
            var stalled = Guid.NewGuid();
            var fresh = Guid.NewGuid();

            db.Claim(stalled).Should().Contain(row => row.Id == id);

            // A dispatcher that died mid-send never clears its claim. Without the lease that
            // message would sit in Sending for ever with nothing to say why.
            db.Execute("UPDATE dbo.EmailOutbox SET ClaimedUtc = DATEADD(MINUTE, -10, SYSUTCDATETIME()) WHERE Id = @id",
                ("@id", id));

            var retaken = db.Claim(fresh).Single(row => row.Id == id);
            retaken.Attempts.Should().Be(2);

            db.RecordSent(id, stalled).Should().Be(0, "the stalled claim no longer holds the row");
            db.RecordSent(id, fresh).Should().Be(1);
            db.Read(id).Status.Should().Be("Sent");
        }
    }

    [SkippableFact]
    public void ALiveLeaseIsNotTakenOver()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var id = db.Enqueue();

            db.Claim(Guid.NewGuid());
            db.Execute("UPDATE dbo.EmailOutbox SET ClaimedUtc = DATEADD(MINUTE, -2, SYSUTCDATETIME()) WHERE Id = @id",
                ("@id", id));

            db.Claim(Guid.NewGuid()).Should().NotContain(row => row.Id == id);
        }
    }

    [SkippableFact]
    public void SendingForgetsACredentialAndKeepsEverythingElse()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var link = db.Enqueue(payload: "ciphertext-of-a-link", isProtected: true);
            var plain = db.Enqueue(payload: """{"company":"Acme"}""");
            var claim = Guid.NewGuid();
            db.Claim(claim);

            db.RecordSent(link, claim);
            db.RecordSent(plain, claim);

            db.Read(link).PayloadJson.Should().BeNull("a delivered link has no further use here");
            db.Read(plain).PayloadJson.Should().Be("""{"company":"Acme"}""");
        }
    }

    [SkippableFact]
    public void AFailureEitherWaitsOrGivesUp()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var retried = db.Enqueue();
            var abandoned = db.Enqueue(payload: "ciphertext-of-a-link", isProtected: true);
            var claim = Guid.NewGuid();
            db.Claim(claim);

            db.RecordFailure(retried, claim, "421 later", retryAfterSeconds: 120).Should().Be(1);
            db.RecordFailure(abandoned, claim, "550 no such user", retryAfterSeconds: null).Should().Be(1);

            var waiting = db.Read(retried);
            waiting.Status.Should().Be("Pending");
            waiting.LastError.Should().Be("421 later");
            waiting.NextAttemptUtc.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(120), TimeSpan.FromSeconds(30));

            var dead = db.Read(abandoned);
            dead.Status.Should().Be("DeadLettered");
            dead.PayloadJson.Should().BeNull("a link nobody will deliver is a link nobody needs kept");
        }
    }

    [SkippableFact]
    public void AMessageWithNoAddressIsRefusedRatherThanQueuedToDeadLetter()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);

            // Raised before anything is written, so it is safe mid-scope.
            var enqueue = () => db.Enqueue(to: "  ");

            enqueue.Should().Throw<SqlException>().Which.Number.Should().Be(NoAddress);
        }
    }

    [SkippableFact]
    public void AnApprovalQueuesItsMessageInTheSameTransaction()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "ada@approval.invalid");

            db.Scalar("EXEC dbo.spAccount_Approve @Id = @id, @ApprovedBy = @by, @CustomerGroupId = NULL, @SiteId = @site",
                ("@id", accountId), ("@by", db.StaffId), ("@site", db.SiteId)).Should().Be(1);

            var row = db.QueuedFor(db.SiteId).Single();
            row.TemplateKey.Should().Be(EmailTemplates.AccountApproved.Key);
            row.ToAddress.Should().Be("ada@approval.invalid");
            row.ToName.Should().Be("Ada Byron");
            row.PayloadProtected.Should().BeFalse();

            // FOR JSON on one side, a C# record on the other. This is what lets them share a shape.
            JsonSerializer.Deserialize<AccountApprovedPayload>(row.PayloadJson!, EmailPayloadJson.Options)!
                .Company.Should().Be("Outbox Approval Co");
        }
    }

    [SkippableFact]
    public void ARejectionQueuesTheReasonAsStored()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "ada@approval.invalid");

            db.Scalar("EXEC dbo.spAccount_Reject @Id = @id, @ApprovedBy = @by, @Reason = @reason, @SiteId = @site",
                ("@id", accountId), ("@by", db.StaffId), ("@reason", "We could not verify the VAT number."),
                ("@site", db.SiteId)).Should().Be(1);

            var row = db.QueuedFor(db.SiteId).Single();
            row.TemplateKey.Should().Be(EmailTemplates.AccountRejected.Key);

            var payload = JsonSerializer.Deserialize<AccountRejectedPayload>(row.PayloadJson!, EmailPayloadJson.Options)!;
            payload.Company.Should().Be("Outbox Approval Co");
            payload.Reason.Should().Be("We could not verify the VAT number.");
        }
    }

    [SkippableFact]
    public void ADecisionSomebodyElseMadeFirstQueuesNothing()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "ada@approval.invalid");
            const string approve = "EXEC dbo.spAccount_Approve @Id = @id, @ApprovedBy = @by, @CustomerGroupId = NULL, @SiteId = @site";

            db.Scalar(approve, ("@id", accountId), ("@by", db.StaffId), ("@site", db.SiteId));

            // The second press is a no-op and a 409; the applicant was told once, by the first.
            db.Scalar(approve, ("@id", accountId), ("@by", db.StaffId), ("@site", db.SiteId)).Should().Be(0);

            db.QueuedFor(db.SiteId).Should().ContainSingle();
        }
    }

    [SkippableFact]
    public void AnAccountWithNoAddressIsApprovedAndNothingIsQueued()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: null);

            db.Scalar("EXEC dbo.spAccount_Approve @Id = @id, @ApprovedBy = @by, @CustomerGroupId = NULL, @SiteId = @site",
                ("@id", accountId), ("@by", db.StaffId), ("@site", db.SiteId)).Should().Be(1);

            db.QueuedFor(db.SiteId).Should().BeEmpty();
        }
    }

    internal sealed record OutboxRow(
        int Id, string Status, int Attempts, string TemplateKey, string ToAddress, string? ToName,
        string? PayloadJson, bool PayloadProtected, string? LastError, DateTime NextAttemptUtc);

    /// <summary>A site, a staff user, and the outbox procedures, inside one rollback scope.</summary>
    internal sealed class OutboxDb
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly string _runId = Guid.NewGuid().ToString("N")[..12];

        public OutboxDb(SqlConnection connection, SqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;

            SiteId = Scalar("""
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Outbox test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);
                """,
                ("@key", $"outbox-{_runId}"),
                ("@domain", $"{_runId}.outbox.invalid"));

            StaffId = $"outbox-staff-{_runId}";

            Execute("""
                INSERT INTO dbo.[User] (UserId, FirstName, LastName, EmailAddress)
                VALUES (@id, N'Outbox', N'Reviewer', @email);
                """, ("@id", StaffId), ("@email", $"{_runId}@outbox-staff.invalid"));
        }

        public int SiteId { get; }
        public string StaffId { get; }

        public int Enqueue(string to = "someone@outbox.invalid", string payload = "{}", bool isProtected = false)
        {
            Execute("""
                EXEC dbo.spEmailOutbox_Enqueue @SiteId = @site, @ToAddress = @to, @ToName = NULL,
                     @TemplateKey = N'account.approved', @PayloadJson = @payload,
                     @PayloadProtected = @protected;
                """,
                ("@site", SiteId), ("@to", to), ("@payload", payload), ("@protected", isProtected));

            return Scalar("SELECT MAX(Id) FROM dbo.EmailOutbox WHERE SiteId = @site", ("@site", SiteId));
        }

        public int PendingAccount(string? email) => Scalar("""
            INSERT INTO dbo.Account (Reference, Company, ContactName, Email, Currency,
                                     PaymentMethod, PaymentTerms, PaymentTermsDays, CreditLimit,
                                     Status, SiteId)
            OUTPUT INSERTED.Id
            VALUES (@reference, N'Outbox Approval Co', N'Ada Byron', @email, N'EUR',
                    N'Card', N'Prepaid', 0, 0, N'Pending', @site);
            """,
            ("@reference", $"AC-O{_runId[..6]}"),
            ("@email", (object?)email ?? DBNull.Value),
            ("@site", SiteId));

        public int Contact(int accountId, string email, string status = "Active") => Scalar("""
            INSERT INTO dbo.Contact (AccountId, FirstName, LastName, Email, RoleInAccount, IsPrimary, [Status])
            OUTPUT INSERTED.Id
            VALUES (@account, N'Buyer', @last, @email, N'Buyer', 0, @status);
            """,
            ("@account", accountId), ("@last", email.Split('@')[0]), ("@email", email), ("@status", status));

        /// <summary>A taxable product this store sells, through a category mapping.</summary>
        public int Product()
        {
            var feedValue = $"MailCategory-{_runId}";

            if (Scalar("SELECT COUNT(*) FROM dbo.CategoryMapping WHERE SiteId = @site AND FeedValue = @value",
                    ("@site", SiteId), ("@value", feedValue)) == 0)
            {
                var categoryId = Scalar("""
                    INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive)
                    OUTPUT INSERTED.Id
                    VALUES (@site, @slug, N'Mail test category', 1, 1);
                    """, ("@site", SiteId), ("@slug", $"mail-{_runId}"));

                Execute("INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId) VALUES (@site, @value, @category)",
                    ("@site", SiteId), ("@value", feedValue), ("@category", categoryId));
            }

            return Scalar("""
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                         QuantityInStock, Delisted, IsTaxable)
                OUTPUT INSERTED.Id
                VALUES (N'Mail fixture', N'Mail fixture.', 100, @sku, @category, 50, 0, 1);
                """, ("@sku", $"MAIL-{_runId}-{Guid.NewGuid():N}"[..40]), ("@category", feedValue));
        }

        /// <summary>A priced quote of 2 x 100, raised for the account by the given contact.</summary>
        public int Quote(int accountId, int? requestedBy, DateTime? expires = null)
        {
            var quoteId = Scalar("""
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, [Status], SiteId,
                                       RequestedByContactId, ExpiresDate)
                OUTPUT INSERTED.Id
                VALUES (@reference, @account, N'EUR', N'Priced', @site, @requestedBy, @expires);
                """,
                ("@reference", $"QT-M{Guid.NewGuid():N}"[..12]),
                ("@account", accountId), ("@site", SiteId),
                ("@requestedBy", (object?)requestedBy ?? DBNull.Value),
                ("@expires", (object?)expires ?? DBNull.Value));

            Execute("""
                INSERT INTO dbo.QuoteLine (QuoteId, ProductId, Quantity, ListPrice, DiscountPct, NetPrice)
                VALUES (@quote, @product, 2, 100, 0, 100);
                """, ("@quote", quoteId), ("@product", Product()));

            return quoteId;
        }

        public string Reference(string table, int id)
        {
            using var command = Command($"SELECT Reference FROM dbo.[{table}] WHERE Id = @id", ("@id", id));

            return (string)command.ExecuteScalar()!;
        }

        public List<OutboxRow> Claim(Guid token)
        {
            using var command = Command(
                "EXEC dbo.spEmailOutbox_Claim @ClaimToken = @token, @BatchSize = 1000, @LeaseMinutes = 5",
                ("@token", token));

            var ids = new List<int>();

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    ids.Add(reader.GetInt32(reader.GetOrdinal("Id")));
                }
            }

            // Read back once the claim's reader is closed: one active result set per connection.
            return ids.Select(Read).ToList();
        }

        public OutboxRow Read(int id) => QueuedWhere("Id = @value", id).Single();

        public List<OutboxRow> QueuedFor(int siteId) => QueuedWhere("SiteId = @value", siteId);

        public int RecordSent(int id, Guid token) => Scalar(
            "EXEC dbo.spEmailOutbox_RecordSent @Id = @id, @ClaimToken = @token",
            ("@id", id), ("@token", token));

        public int RecordFailure(int id, Guid token, string error, int? retryAfterSeconds) => Scalar(
            "EXEC dbo.spEmailOutbox_RecordFailure @Id = @id, @ClaimToken = @token, @Error = @error, @RetryAfterSeconds = @retry",
            ("@id", id), ("@token", token), ("@error", error),
            ("@retry", (object?)retryAfterSeconds ?? DBNull.Value));

        private List<OutboxRow> QueuedWhere(string predicate, int value)
        {
            using var command = Command($"""
                SELECT Id, Status, Attempts, TemplateKey, ToAddress, ToName, PayloadJson,
                       PayloadProtected, LastError, NextAttemptUtc
                FROM dbo.EmailOutbox WHERE {predicate}
                ORDER BY Id;
                """, ("@value", value));

            using var reader = command.ExecuteReader();
            var rows = new List<OutboxRow>();

            while (reader.Read())
            {
                rows.Add(new OutboxRow(
                    reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetBoolean(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetDateTime(9)));
            }

            return rows;
        }

        public int Scalar(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);

            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void Execute(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);

            command.ExecuteNonQuery();
        }

        private SqlCommand Command(string sql, params (string Name, object Value)[] parameters)
        {
            var command = new SqlCommand(sql, _connection, _transaction);

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return command;
        }
    }
}
