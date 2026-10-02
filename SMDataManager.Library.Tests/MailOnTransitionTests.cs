using System.Text.Json;
using FluentAssertions;
using SMDataManager.Library.Email;
using Xunit;
using OutboxDb = SMDataManager.Library.Tests.EmailOutboxTests.OutboxDb;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Who each quote and order transition writes to, and that what it writes is what the
/// template reads.
/// </summary>
/// <remarks>
/// Each of these procedures builds its payload with <c>FOR JSON</c> and the dispatcher reads it
/// back through a C# record, hours later, in another host. A renamed column or a misspelt
/// alias fails nowhere — the customer just receives a blank where the total should be — so
/// every procedure that queues is read back here through the record its template uses.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class MailOnTransitionTests
{
    [SkippableFact]
    public void SubmittingARequestTellsTheBuyerWhoSentIt()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "office@company.invalid");
            var buyer = db.Contact(accountId, "second.buyer@company.invalid");
            var productId = db.Product();

            db.Execute($"""
                DECLARE @lines dbo.QuoteRequestLine;
                INSERT INTO @lines VALUES ({productId}, 3, 100, 0, 100);
                DECLARE @Id int, @Reference nvarchar(20);
                EXEC dbo.spQuote_SubmitRequest @ContactId = @contact, @SiteId = @site,
                     @Lines = @lines, @Id = @Id OUTPUT, @Reference = @Reference OUTPUT;
                """, ("@contact", buyer), ("@site", db.SiteId));

            var row = db.QueuedFor(db.SiteId).Single();
            row.TemplateKey.Should().Be(EmailTemplates.QuoteReceived.Key);
            // The buyer who sent it, not whoever registered the company.
            row.ToAddress.Should().Be("second.buyer@company.invalid");

            var payload = Read<QuoteReceivedPayload>(row);
            payload.Reference.Should().StartWith("QT-");
            payload.Lines.Should().Be(1);
        }
    }

    [SkippableFact]
    public void PricingAQuoteTellsTheBuyerWhoAskedForIt()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "office@company.invalid");
            var buyer = db.Contact(accountId, "buyer@company.invalid");
            var quoteId = db.Quote(accountId, requestedBy: buyer, expires: new DateTime(2026, 12, 31));

            db.Scalar("EXEC dbo.spQuote_Price @QuoteId = @quote, @SiteId = @site",
                ("@quote", quoteId), ("@site", db.SiteId)).Should().Be(1);

            var row = db.QueuedFor(db.SiteId).Single();
            row.TemplateKey.Should().Be(EmailTemplates.QuotePriced.Key);
            row.ToAddress.Should().Be("buyer@company.invalid");

            var payload = Read<QuotePricedPayload>(row);
            payload.Reference.Should().Be(db.Reference("Quote", quoteId));
            payload.Value.Should().Be(200m, "the net total of the lines, as the quote page shows it");
            payload.ExpiresDate.Should().Be(new DateTime(2026, 12, 31));
        }
    }

    [SkippableTheory]
    // Keyed in by staff: nobody asked, so the account's own address.
    [InlineData(false)]
    // The buyer has left the company. Their mailbox is the wrong place for the price.
    [InlineData(true)]
    public void WithNoActiveRequesterTheAccountsOwnAddressIsTold(bool requesterDisabled)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "office@company.invalid");
            int? requester = requesterDisabled
                ? db.Contact(accountId, "gone@company.invalid", status: "Disabled")
                : null;
            var quoteId = db.Quote(accountId, requestedBy: requester);

            db.Scalar("EXEC dbo.spQuote_Price @QuoteId = @quote, @SiteId = @site",
                ("@quote", quoteId), ("@site", db.SiteId));

            var row = db.QueuedFor(db.SiteId).Single();
            row.ToAddress.Should().Be("office@company.invalid");
            row.ToName.Should().Be("Ada Byron");
        }
    }

    [SkippableFact]
    public void AnAcceptedOrderIsConfirmedToTheBuyerWhoAcceptedIt()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "office@company.invalid");
            var asked = db.Contact(accountId, "asked@company.invalid");
            var accepted = db.Contact(accountId, "accepted@company.invalid");
            var quoteId = db.Quote(accountId, requestedBy: asked);

            db.Execute("""
                DECLARE @Id int, @Reference nvarchar(20);
                EXEC dbo.spOrder_ConvertFromQuote @QuoteId = @quote, @Id = @Id OUTPUT,
                     @Reference = @Reference OUTPUT, @SiteId = @site, @PlacedByContactId = @placer,
                     @PoNumber = N'PO-77', @TaxTreatment = N'Domestic standard',
                     @TaxLegend = N'VAT charged at 23%.', @TaxRatePct = 23;
                """, ("@quote", quoteId), ("@site", db.SiteId), ("@placer", accepted));

            var row = db.QueuedFor(db.SiteId).Single();
            row.TemplateKey.Should().Be(EmailTemplates.OrderConfirmed.Key);
            row.ToAddress.Should().Be("accepted@company.invalid");

            // Exactly what the order stored, read back through the record the template uses.
            var payload = Read<OrderConfirmedPayload>(row);
            payload.Reference.Should().StartWith("SO-");
            payload.QuoteReference.Should().Be(db.Reference("Quote", quoteId));
            payload.PoNumber.Should().Be("PO-77");
            payload.SubTotal.Should().Be(200m);
            payload.Tax.Should().Be(46m);
            payload.Total.Should().Be(246m);
            payload.TaxTreatment.Should().Be("Domestic standard");
            payload.TaxLegend.Should().Be("VAT charged at 23%.");
            payload.DueDate.Should().Be(DateTime.UtcNow.Date);
        }
    }

    [SkippableFact]
    public void AStaffConversionIsConfirmedToWhoeverAskedForTheQuote()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var accountId = db.PendingAccount(email: "office@company.invalid");
            var asked = db.Contact(accountId, "asked@company.invalid");
            var quoteId = db.Quote(accountId, requestedBy: asked);

            // An order on a customer's account is theirs to hear about, whoever keyed it in.
            db.Execute("""
                DECLARE @Id int, @Reference nvarchar(20);
                EXEC dbo.spOrder_ConvertFromQuote @QuoteId = @quote, @Id = @Id OUTPUT,
                     @Reference = @Reference OUTPUT, @SiteId = @site, @StaffId = @staff;
                """, ("@quote", quoteId), ("@site", db.SiteId), ("@staff", db.StaffId));

            db.QueuedFor(db.SiteId).Single().ToAddress.Should().Be("asked@company.invalid");
        }
    }

    [SkippableFact]
    public void AStoresWordingIsReadForThatStoreOnly()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var mine = new OutboxDb(connection, transaction);
            var theirs = new OutboxDb(connection, transaction);

            mine.Execute("""
                INSERT INTO dbo.SiteEmailTemplate (SiteId, TemplateKey, Subject, Body)
                VALUES (@site, N'account.approved', N'Mine', NULL);
                """, ("@site", mine.SiteId));

            WordingCount(mine, mine.SiteId).Should().Be(1);
            WordingCount(theirs, theirs.SiteId).Should().Be(0, "another store's wording is not this one's");
        }
    }

    // One batch: a parameterised command runs under sp_executesql, and a temp table made in
    // one would be gone before the next could read it.
    private static int WordingCount(OutboxDb db, int siteId) => db.Scalar("""
        DECLARE @wording TABLE (SiteId int, TemplateKey nvarchar(80), Subject nvarchar(200), Body nvarchar(max));
        INSERT INTO @wording EXEC dbo.spSiteEmailTemplate_Get @SiteId = @site, @TemplateKey = N'account.approved';
        SELECT COUNT(*) FROM @wording;
        """, ("@site", siteId));

    private static T Read<T>(EmailOutboxTests.OutboxRow row) =>
        JsonSerializer.Deserialize<T>(row.PayloadJson!, EmailPayloadJson.Options)!;
}
