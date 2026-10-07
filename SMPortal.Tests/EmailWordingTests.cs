using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Services;
using Xunit;
using Email = SMPortal.Pages.Admin.Store.Email;

namespace SMPortal.Tests;

// The email wording screen (T9): dbo.SiteEmailTemplate was written by hand before it existed.
public class EmailWordingTests : TestContext
{
    private readonly IAdminDataService _data = Substitute.For<IAdminDataService>();

    public EmailWordingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_data);
        Services.AddSingleton(Substitute.For<IToastService>());

        _data.GetEmailWording().Returns(new List<EmailWording>
        {
            new()
            {
                Key = "password.reset", Audience = "Customer", PlatformSubject = "Reset your password",
                PlatformBody = "Use {ResetLink}.", Placeholders = ["ResetLink", "SiteName"], Required = ["ResetLink"]
            },
            new()
            {
                Key = "operator.feed-failed", Audience = "Operator", PlatformSubject = "Feed failed",
                PlatformBody = "{FeedName}: {Message}", Placeholders = ["FeedName", "Message"], Customised = true,
                Subject = "Our feed broke", Body = "{FeedName} said {Message}"
            }
        });
    }

    [Fact]
    public void EveryMessageIsListedWithWhoseWordsItUses()
    {
        var cut = RenderComponent<Email>();

        cut.FindAll(".email-message").Select(message => message.TextContent).Should().SatisfyRespectively(
            reset => reset.Should().Contain("Password reset").And.Contain("To customers").And.Contain("Platform"),
            feed => feed.Should().Contain("Operator feed failed").And.Contain("operator").And.Contain("Store's own"));
    }

    [Fact]
    public void TheRequiredPlaceholderIsMarkedAndTheRefusalShownInTheAdminsWords()
    {
        _data.SaveEmailWording(default!, default, default).ReturnsForAnyArgs(
            ((EmailWording?)null, "This wording cannot be used: it leaves out {ResetLink}, which password.reset cannot do without."));
        var cut = RenderComponent<Email>();

        cut.Find(".email-token--required").TextContent.Should().Be("{ResetLink}");

        cut.Find("#email-body").Change("Click the link.");
        cut.Find("button.email-save").Click();

        _data.Received(1).SaveEmailWording("password.reset", null, "Click the link.");
        cut.Find(".email-status").TextContent.Should().Contain("leaves out {ResetLink}");
    }

    [Fact]
    public void AStoresOwnWordingCanBeTakenBackToThePlatforms()
    {
        _data.SaveEmailWording(default!, default, default).ReturnsForAnyArgs(
            (new EmailWording { Key = "operator.feed-failed", Audience = "Operator", Customised = false }, (string?)null));
        var cut = RenderComponent<Email>();

        cut.Find(".email-message[data-key='operator.feed-failed']").Click();
        cut.Find("button.email-revert").Click();

        _data.Received(1).SaveEmailWording("operator.feed-failed", null, null);
        cut.FindAll("button.email-revert").Should().BeEmpty();
    }
}
