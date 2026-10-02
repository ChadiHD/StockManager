using FluentAssertions;
using SMDataManager.Library.Email;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// The platform wording, and the one rule that keeps a customer's words from becoming template.
/// </summary>
/// <remarks>
/// A template is rendered hours after it was queued, by a host that did not queue it, from a
/// payload that may have been written by a procedure. None of those steps fails loudly when a
/// placeholder is misspelt: the customer simply receives <c>{Compnay}</c>. So the guards here
/// are over every template at once, and a new one is covered by being registered.
/// </remarks>
public class EmailTemplateTests
{
    private static readonly SiteModel Site = new()
    {
        Id = 1,
        SiteKey = "test",
        Name = "Test Store",
        Domain = "shop.test.example",
        CurrencyCode = "EUR",
        Locale = "en-IE",
    };

    public static TheoryData<string> Keys()
    {
        var keys = new TheoryData<string>();

        foreach (var template in EmailTemplates.All)
        {
            keys.Add(template.Key);
        }

        return keys;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void EveryPlaceholderInThePlatformWordingHasAValue(string key)
    {
        var template = EmailTemplates.Find(key);

        // An empty payload is the worst case: every value comes from a null, and a template
        // whose values cannot cope with that has a message that renders as an exception.
        var rendered = EmailRenderer.Render(template, Site, "{}");

        EmailRenderer.PlaceholdersIn(rendered.Subject).Should().BeEmpty();
        EmailRenderer.PlaceholdersIn(rendered.Body).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void ThePlatformWordingUsesEveryPlaceholderItRequiresOfAStore(string key)
    {
        var template = EmailTemplates.Find(key);

        template.RequiredTokens
            .Except(EmailRenderer.PlaceholdersIn(template.DefaultBody))
            .Should().BeEmpty();
    }

    [Fact]
    public void KeysAreUnique()
    {
        // Stored in dbo.EmailOutbox, so a duplicate is two templates fighting over one row.
        EmailTemplates.All.Select(template => template.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AnUnknownKeyIsNotFound()
    {
        EmailTemplates.Find("no.such.template").Should().BeNull();
        EmailTemplates.Find(null!).Should().BeNull();
    }

    [Fact]
    public void ACustomersWordsAreNeverReadAsTemplate()
    {
        /*
        A rejection reason is typed by staff and an applicant's company name by the applicant,
        and both reach a message. If a value were scanned for placeholders after substitution,
        a company called "{SignInLink}" would be sent somebody's link, and a reason containing
        a placeholder would expand into whatever value sat behind it. One pass is the rule.
        */
        var payload = EmailTemplates.AccountRejected.Serialize(
            new AccountRejectedPayload("{SiteDomain} Ltd", "Your {Reason} was {Company}."));

        var rendered = EmailRenderer.Render(EmailTemplates.AccountRejected, Site, payload);

        rendered.Body.Should().Contain("{SiteDomain} Ltd");
        rendered.Body.Should().Contain("Your {Reason} was {Company}.");
        rendered.Body.Should().NotContain("shop.test.example Ltd");
    }

    [Fact]
    public void TheAcknowledgementCarriesTheLinkWhenThereIsOne()
    {
        var payload = EmailTemplates.RegistrationReceived.Serialize(
            new RegistrationReceivedPayload("AC-0042", "https://shop.test.example/confirm-email?t=1"));

        var rendered = EmailRenderer.Render(EmailTemplates.RegistrationReceived, Site, payload);

        rendered.Subject.Should().Be("We have your application — Test Store");
        rendered.Body.Should().Contain("Your reference is AC-0042.");
        rendered.Body.Should().Contain("https://shop.test.example/confirm-email?t=1");
    }

    [Fact]
    public void TheAcknowledgementReadsTheSameWithNoReferenceAndNoLink()
    {
        // The neutral answer to a duplicate. "We have sent you a link" over a message carrying
        // none would tell the reader their address is the one with an account.
        var payload = EmailTemplates.RegistrationReceived.Serialize(
            new RegistrationReceivedPayload(null!, null!));

        var rendered = EmailRenderer.Render(EmailTemplates.RegistrationReceived, Site, payload);

        rendered.Body.Should().NotContain("reference");
        rendered.Body.Should().NotContain("link");
        rendered.Body.Should().NotContain("\n\n\n", "an empty fragment must not leave a hole");
        rendered.Body.Should().StartWith("Thanks for applying for a trade account with Test Store.");
    }

    [Fact]
    public void ApprovalPointsAtThisStoresSignInPage()
    {
        var payload = EmailTemplates.AccountApproved.Serialize(new AccountApprovedPayload("Acme Trading"));

        var rendered = EmailRenderer.Render(EmailTemplates.AccountApproved, Site, payload);

        rendered.Body.Should().Contain("Your application for Acme Trading has been approved.");
        rendered.Body.Should().Contain("https://shop.test.example/login");
    }

    [Fact]
    public void ExactlyTheTemplatesCarryingATokenLinkAreStoredEncrypted()
    {
        // The three whose payload holds a link Identity minted. A new one that carries a token
        // and forgets the flag would put a way into an account in plain text in a table that
        // is backed up and restored; this list is the place that has to change with it.
        EmailTemplates.All
            .Where(template => template.CarriesCredential)
            .Select(template => template.Key)
            .Should().BeEquivalentTo(
                "registration.received", "registration.confirmation-reminder", "password.reset");
    }

    [Fact]
    public void AnOrderConfirmationLinksToTheDocumentAndSaysWhyNothingWasCharged()
    {
        var payload = EmailTemplates.OrderConfirmed.Serialize(new OrderConfirmedPayload(
            "SO-0012", "QT-0041", "PO-77", 1000m, 0m, 1000m,
            "Intra-EU reverse charge",
            "Reverse charge: VAT to be accounted for by the recipient (Article 196, Directive 2006/112/EC).",
            new DateTime(2026, 11, 1)));

        var rendered = EmailRenderer.Render(EmailTemplates.OrderConfirmed, Site, payload);

        rendered.Subject.Should().Be("Order SO-0012 is confirmed — Test Store");
        rendered.Body.Should().Contain("Raised from quote QT-0041.");
        rendered.Body.Should().Contain("Your purchase-order number: PO-77.");
        rendered.Body.Should().Contain("Net: €1,000.00");
        // Labelled by treatment: "VAT €0.00" with no reason is what an accountant sends back.
        rendered.Body.Should().Contain("Intra-EU reverse charge: €0.00");
        rendered.Body.Should().Contain("Article 196");
        rendered.Body.Should().Contain("Payment is due by 1 Nov 2026.");
        // Linked, not attached: T6's renderer decision.
        rendered.Body.Should().Contain("https://shop.test.example/account/orders/SO-0012/print");
    }

    [Fact]
    public void APricedQuoteIsNetAndSaysTaxComesLater()
    {
        var payload = EmailTemplates.QuotePriced.Serialize(
            new QuotePricedPayload("QT-0041", 1234.5m, new DateTime(2026, 10, 30)));

        var rendered = EmailRenderer.Render(EmailTemplates.QuotePriced, Site, payload);

        rendered.Body.Should().Contain("priced at €1,234.50 before tax");
        rendered.Body.Should().Contain("It is valid until 30 Oct 2026.");
        rendered.Body.Should().Contain("https://shop.test.example/account/quotes/QT-0041");
    }

    [Fact]
    public void AStoresOwnWordingReplacesThePlatforms()
    {
        var wording = Wording(EmailTemplates.AccountRejected,
            subject: "Your application to {SiteName}",
            body: "Sorry, {Company}. {Reason}");

        var rendered = EmailRenderer.Render(EmailTemplates.AccountRejected, Site,
            EmailTemplates.AccountRejected.Serialize(new AccountRejectedPayload("Acme", "No VAT number.")),
            wording);

        rendered.WordingRefused.Should().BeNull();
        rendered.Subject.Should().Be("Your application to Test Store");
        rendered.Body.Should().Be("Sorry, Acme. No VAT number.\n");
    }

    [Fact]
    public void AHalfLeftBlankKeepsThePlatformsWordsForThatHalf()
    {
        var wording = Wording(EmailTemplates.AccountApproved, subject: "Welcome to {SiteName}", body: null);

        var rendered = EmailRenderer.Render(EmailTemplates.AccountApproved, Site,
            EmailTemplates.AccountApproved.Serialize(new AccountApprovedPayload("Acme")), wording);

        rendered.Subject.Should().Be("Welcome to Test Store");
        rendered.Body.Should().StartWith("Your application for Acme has been approved.");
    }

    [Fact]
    public void WordingNamingAPlaceholderTheMessageDoesNotHaveIsRefused()
    {
        // The customer would otherwise receive "{Compnay}". Refused whole, so a subject and a
        // body written to go together are not mixed with the platform's.
        var wording = Wording(EmailTemplates.AccountApproved, subject: "Hello {Compnay}", body: "Hi {Company}");

        var rendered = EmailRenderer.Render(EmailTemplates.AccountApproved, Site,
            EmailTemplates.AccountApproved.Serialize(new AccountApprovedPayload("Acme")), wording);

        rendered.WordingRefused.Should().Contain("{Compnay}");
        rendered.Subject.Should().Be("Your trade account is open — Test Store");
        rendered.Body.Should().StartWith("Your application for Acme has been approved.");
    }

    [Fact]
    public void WordingThatLeavesOutTheLinkIsRefused()
    {
        // A reset mail with no link is a message telling somebody to click nothing.
        var wording = Wording(EmailTemplates.PasswordReset, subject: null,
            body: "Somebody asked to reset your password at {SiteName}.");

        var rendered = EmailRenderer.Render(EmailTemplates.PasswordReset, Site,
            EmailTemplates.PasswordReset.Serialize(new PasswordResetPayload("https://shop.test.example/reset-password?t=1")),
            wording);

        rendered.WordingRefused.Should().Contain("{ResetLink}");
        rendered.Body.Should().Contain("https://shop.test.example/reset-password?t=1");
    }

    [Fact]
    public void ACustomersWordsAreNotReadAsTemplateInAStoresWordingEither()
    {
        var wording = Wording(EmailTemplates.AccountRejected, subject: null, body: "{Reason}");

        var rendered = EmailRenderer.Render(EmailTemplates.AccountRejected, Site,
            EmailTemplates.AccountRejected.Serialize(new AccountRejectedPayload("Acme", "See {SiteDomain}")),
            wording);

        rendered.Body.Should().Be("See {SiteDomain}\n");
    }

    private static SiteEmailTemplateModel Wording(EmailTemplate template, string? subject, string? body) => new()
    {
        SiteId = Site.Id,
        TemplateKey = template.Key,
        Subject = subject!,
        Body = body!,
    };

    [Fact]
    public void TheUndeliverableAlertNamesEachMessageAndNeverItsContent()
    {
        var payload = EmailTemplates.MailUndeliverable.Serialize(new MailUndeliverablePayload(
        [
            new UndeliverableMessage(17, "registration.received", "ada@example.com", 8, "550 mailbox unavailable"),
            new UndeliverableMessage(18, "account.approved", "bob@example.com", 8, "550 mailbox unavailable"),
        ]));

        var rendered = EmailRenderer.Render(EmailTemplates.MailUndeliverable, Site, payload);

        rendered.Body.Should().Contain("2 message(s) from Test Store");
        rendered.Body.Should().Contain("#17 registration.received to ada@example.com, after 8 attempt(s): 550 mailbox unavailable");
        rendered.Body.Should().Contain("#18 account.approved to bob@example.com");
        EmailTemplates.MailUndeliverable.Audience.Should().Be(EmailAudience.Operator);
    }
}
