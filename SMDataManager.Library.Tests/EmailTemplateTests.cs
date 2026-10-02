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
    public void OnlyTemplatesCarryingALinkAreStoredEncrypted()
    {
        EmailTemplates.RegistrationReceived.CarriesCredential.Should().BeTrue();
        EmailTemplates.AccountApproved.CarriesCredential.Should().BeFalse();
        EmailTemplates.AccountRejected.CarriesCredential.Should().BeFalse();
    }

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
