using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Ordering;
using SMStore.Sites;
using SMStore.Tests.TestSupport;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// SiteHeader is the one place sign-in state changes what gets rendered rather than just what
/// it says: a signed-in visitor gets a sign-out form, not a link, because a GET that ends a
/// session can be triggered by anything that makes the browser fetch a URL. That distinction,
/// and the active-link logic beside it, are worth testing directly.
/// </summary>
public class SiteHeaderTests : Bunit.TestContext
{
    /// <summary>Renders the antiforgery hidden input a real request would carry.</summary>
    private sealed class StubAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() =>
            new("stub-token-value", "__RequestVerificationToken");
    }

    private static SiteModel SiteWith(string name = "Test store") =>
        new() { Id = 1, SiteKey = "test", Name = name, OrderMode = RfqOrderingMode.ModeKey };

    [Fact]
    public void ASignedInCustomerSeesASignOutFormRatherThanALink()
    {
        var customer = Substitute.For<ICustomerContext>();
        customer.IsSignedIn.Returns(true);
        customer.Contact.Returns(new ContactModel { FirstName = "Ada", LastName = "Byron" });
        Services.AddSingleton<AntiforgeryStateProvider>(new StubAntiforgeryStateProvider());

        var cut = SiteHeaderHarness.Render(this, SiteWith(), new RfqOrderingMode(), customer);

        var form = cut.Find("form.site-header__account");
        form.GetAttribute("method").Should().Be("post");
        form.GetAttribute("action").Should().Be(CustomerAuthentication.LogoutPath);

        // The antiforgery token is what stops this being a plain, forgeable POST.
        form.QuerySelector("input[type=hidden][name='__RequestVerificationToken']").Should().NotBeNull();

        cut.FindAll("a.site-header__signin").Should().BeEmpty();
    }

    [Fact]
    public void AnAnonymousVisitorSeesASignInLinkRatherThanAForm()
    {
        var customer = Substitute.For<ICustomerContext>();
        customer.IsSignedIn.Returns(false);

        var cut = SiteHeaderHarness.Render(this, SiteWith(), new RfqOrderingMode(), customer);

        var link = cut.Find("a.site-header__signin");
        link.GetAttribute("href").Should().Be("/login");

        cut.FindAll("form.site-header__account").Should().BeEmpty();
    }

    [Fact]
    public void MarksTheMatchingNavLinkAsCurrentByPathAlone()
    {
        // bUnit locks its Services container against further registrations the moment
        // anything is resolved from it, which a component render does implicitly — so the
        // NavigationManager has to be fetched (and navigated) after rendering, not before.
        var cut = SiteHeaderHarness.Render(this, SiteWith(), new RfqOrderingMode());
        Services.GetRequiredService<NavigationManager>().NavigateTo("/catalog");
        cut.Render();

        cut.Find(".site-header__links a[href='/catalog']").GetAttribute("aria-current").Should().Be("page");
        cut.Find(".site-header__links a[href='/']").HasAttribute("aria-current").Should().BeFalse();
        cut.Find(".site-header__links a[href='/solutions']").HasAttribute("aria-current").Should().BeFalse();
    }

    [Fact]
    public void HomeIsCurrentAtTheRoot()
    {
        // The default FakeNavigationManager starts at its base address, which is the root --
        // no navigation needed to exercise the true side of Current()'s "/" special case.
        var cut = SiteHeaderHarness.Render(this, SiteWith(), new RfqOrderingMode());

        cut.Find(".site-header__links a[href='/']").GetAttribute("aria-current").Should().Be("page");
    }

    [Fact]
    public void HomeIsCurrentOnlyAtTheRootAndNotOnEveryPage()
    {
        // "/" must match exactly, or it lights up for every page on the site — the one case
        // Current() special-cases rather than relying on the general StartsWith rule.
        var cut = SiteHeaderHarness.Render(this, SiteWith(), new RfqOrderingMode());
        Services.GetRequiredService<NavigationManager>().NavigateTo("/solutions");
        cut.Render();

        cut.Find(".site-header__links a[href='/']").HasAttribute("aria-current").Should().BeFalse();
    }

    [Fact]
    public void AQueryStringDoesNotStopAPageMatchingItsNavLink()
    {
        var cut = SiteHeaderHarness.Render(this, SiteWith(), new RfqOrderingMode());
        Services.GetRequiredService<NavigationManager>().NavigateTo("/catalog?category=cables");
        cut.Render();

        cut.Find(".site-header__links a[href='/catalog']").GetAttribute("aria-current").Should().Be("page");
    }
}
