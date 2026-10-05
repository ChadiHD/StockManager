using System.Text.Json;
using FluentAssertions;
using SMDataManager.Library.Email;
using Xunit;
using OutboxDb = SMDataManager.Library.Tests.EmailOutboxTests.OutboxDb;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Which quotes the nightly sweep reminds a customer about, and that it does so once.
/// </summary>
/// <remarks>
/// A database test because the whole rule is one UPDATE's WHERE clause, and the claim is that
/// UPDATE's locking. Every quote here belongs to a site the test made, and the procedure is
/// scoped by site, so a development database's real quotes are never touched.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class QuoteExpiryTests
{
    [SkippableFact]
    public void APricedQuoteAboutToLapseIsRemindedOnce()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "office@company.invalid");
            var buyer = db.Contact(accountId, "buyer@company.invalid");
            var expires = DateTime.UtcNow.AddDays(2);
            var quoteId = db.Quote(accountId, requestedBy: buyer, expires: expires);

            Sweep(db).Should().Be(1);
            // The second night finds it stamped. Two replicas at the same hour are the same case.
            Sweep(db).Should().Be(0);

            var row = db.QueuedFor(db.SiteId).Single();
            row.TemplateKey.Should().Be(EmailTemplates.QuoteExpiring.Key);
            row.ToAddress.Should().Be("buyer@company.invalid");

            var payload = JsonSerializer.Deserialize<QuoteExpiringPayload>(row.PayloadJson!, EmailPayloadJson.Options)!;
            payload.Reference.Should().Be(db.Reference("Quote", quoteId));
            payload.Value.Should().Be(200m);
            payload.ExpiresDate.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
        }
    }

    [SkippableTheory]
    // Further out than the window: tomorrow's sweep, or the one after.
    [InlineData("Priced", 10.0)]
    // Already gone. "Expires soon" after it has expired can only be wrong.
    [InlineData("Priced", -1.0)]
    // Nothing to lose yet, or already answered.
    [InlineData("Requested", 2.0)]
    [InlineData("Accepted", 2.0)]
    [InlineData("Rejected", 2.0)]
    public void OnlyAnUnansweredPricedQuoteInsideTheWindowIsReminded(string status, double daysToExpiry)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "office@company.invalid");
            var quoteId = db.Quote(accountId, requestedBy: null, expires: DateTime.UtcNow.AddDays(daysToExpiry));
            db.Execute("UPDATE dbo.Quote SET Status = @status WHERE Id = @id", ("@status", status), ("@id", quoteId));

            Sweep(db).Should().Be(0);
            db.QueuedFor(db.SiteId).Should().BeEmpty();
        }
    }

    [SkippableFact]
    public void AQuoteWithNoExpiryIsNeverReminded()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "office@company.invalid");
            db.Quote(accountId, requestedBy: null, expires: null);

            // No date means the store did not set one, not that it is about to pass.
            Sweep(db).Should().Be(0);
        }
    }

    [SkippableFact]
    public void RepricingAQuoteEarnsItANewReminder()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "office@company.invalid");
            var quoteId = db.Quote(accountId, requestedBy: null, expires: DateTime.UtcNow.AddDays(2));

            Sweep(db).Should().Be(1);

            // A re-sent price is a new statement to the customer, with its own date.
            db.Scalar("EXEC dbo.spQuote_Price @QuoteId = @quote, @SiteId = @site",
                ("@quote", quoteId), ("@site", db.SiteId));

            Sweep(db).Should().Be(1);
        }
    }

    [SkippableFact]
    public void ACustomerWithNobodyToTellIsStampedSoTheyAreNotLookedAtNightly()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: null);
            db.Quote(accountId, requestedBy: null, expires: DateTime.UtcNow.AddDays(2));

            Sweep(db).Should().Be(0, "there is no address to queue to");
            db.QueuedFor(db.SiteId).Should().BeEmpty();
            db.Scalar("SELECT COUNT(*) FROM dbo.Quote WHERE SiteId = @site AND ExpiryNoticeSentUtc IS NOT NULL",
                ("@site", db.SiteId)).Should().Be(1);
        }
    }

    private static int Sweep(OutboxDb db) => db.Scalar(
        "EXEC dbo.spQuote_QueueExpiryNotices @SiteId = @site, @WithinDays = 3", ("@site", db.SiteId));
}
