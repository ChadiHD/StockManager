using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Services;
using Xunit;
using Content = SMPortal.Pages.Admin.Store.Content;

namespace SMPortal.Tests;

// The content pages screen (T9): dbo.SiteContent was written by hand before it existed.
public class ContentPagesTests : TestContext
{
    private readonly IAdminDataService _data = Substitute.For<IAdminDataService>();

    public ContentPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_data);
        Services.AddSingleton(Substitute.For<IToastService>());

        _data.GetContentPages().Returns(new List<ContentPageItem>
        {
            new() { Key = "home", Label = "Home page", Written = true, Title = "Welcome", BodyHtml = "<p>Hi</p>", LastModified = DateTime.UtcNow },
            new() { Key = "terms", Label = "Terms of sale", Written = false }
        });
    }

    [Fact]
    public void EveryPageIsListedWithWhetherTheStoreHasWrittenIt()
    {
        var cut = RenderComponent<Content>();

        cut.FindAll(".content-page").Select(page => page.TextContent).Should().SatisfyRespectively(
            home => home.Should().Contain("Home page").And.Contain("Published"),
            terms => terms.Should().Contain("Terms of sale").And.Contain("Not written"));
    }

    [Fact]
    public void SavingSendsThePageAndShowsWhenTheAllowListRemovedSomething()
    {
        _data.SaveContentPage(default!, default!, default, default).ReturnsForAnyArgs(
            (new ContentPageItem { Key = "terms", Label = "Terms of sale", Written = true, Title = "Terms", BodyHtml = "<p>Pay in 30 days.</p>" }, (string?)null));
        var cut = RenderComponent<Content>();

        cut.Find(".content-page[data-key=terms]").Click();
        cut.Find("#content-title").Change("Terms");
        cut.Find("#content-body").Change("<p>Pay in 30 days.</p><script>x()</script>");
        cut.Find("button.content-save-button").Click();

        _data.Received(1).SaveContentPage("terms", "Terms", null, "<p>Pay in 30 days.</p><script>x()</script>");
        // The box now holds what was stored, and the admin is told it is not what they typed.
        cut.Find("#content-body").TextContent.Should().Be("<p>Pay in 30 days.</p>");
        cut.Find(".content-status").TextContent.Should().Contain("has been removed");
    }

    [Fact]
    public void ARefusalIsShownAndNothingIsReplaced()
    {
        _data.SaveContentPage(default!, default!, default, default).ReturnsForAnyArgs(
            ((ContentPageItem?)null, "A page needs a title of up to 200 characters."));
        var cut = RenderComponent<Content>();

        cut.Find("#content-title").Change("");
        cut.Find("button.content-save-button").Click();

        cut.Find(".content-status").TextContent.Should().Contain("needs a title");
        cut.Find("#content-body").TextContent.Should().Be("<p>Hi</p>");
    }
}
