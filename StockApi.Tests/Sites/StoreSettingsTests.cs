using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Sites;

/// <summary>
/// The store settings screen (T9). Until it existed every one of these values was set by
/// updating the row by hand, and the only validation was a provider throwing on the store's next
/// request. These are the refusals that keep a saved store working.
/// </summary>
public class StoreSettingsTests
{
    private static SiteModel Valid() => new()
    {
        Name = "  Acme Trade  ",
        Domain = "Shop.Example.COM",
        Country = "ie",
        CurrencyCode = "eur",
        Locale = "en-IE",
        OrderMode = "rfq",
        RegistrationFieldSet = "EU-B2B",
        TaxRuleSet = "eu-b2b",
        PriceDisplay = "authenticated",
        OperatorEmail = "ops@example.com",
        MailFromAddress = "no-reply@example.com"
    };

    [Fact]
    public void AValidStoreIsAcceptedAndSavedInTheSpellingItsKeysUse()
    {
        var site = Valid();

        SiteSettingsRules.WhyRefused(site).Should().BeNull();

        // CatalogPresenter compares PriceDisplay ordinally; "authenticated" saved as typed would
        // have shown a trade store's prices to everybody.
        site.PriceDisplay.Should().Be("Authenticated");
        site.OrderMode.Should().Be("Rfq");
        site.RegistrationFieldSet.Should().Be("eu-b2b");
        site.Domain.Should().Be("shop.example.com");
        site.Country.Should().Be("IE");
        site.CurrencyCode.Should().Be("EUR");
        site.Name.Should().Be("Acme Trade");
    }

    [Theory]
    [InlineData("https://shop.example.com")]
    [InlineData("shop.example.com/catalog")]
    [InlineData("shop.example.com:443")]
    [InlineData("")]
    public void ADomainIsAHostNameAndNothingElse(string domain)
    {
        // SiteResolutionMiddleware compares the request's Host with this; anything else matches
        // nothing, and the store would answer 404 to every customer.
        var site = Valid();
        site.Domain = domain;

        SiteSettingsRules.WhyRefused(site).Should().Contain("host name");
    }

    [Theory]
    [InlineData(nameof(SiteModel.OrderMode), "DirectCheckout")]
    [InlineData(nameof(SiteModel.RegistrationFieldSet), "us-b2b")]
    [InlineData(nameof(SiteModel.TaxRuleSet), "vat-free")]
    [InlineData(nameof(SiteModel.PriceDisplay), "Members")]
    public void AKeyNothingImplementsIsRefusedRatherThanSavedIntoA500(string property, string value)
    {
        var site = Valid();
        typeof(SiteModel).GetProperty(property)!.SetValue(site, value);

        SiteSettingsRules.WhyRefused(site).Should().NotBeNull();
    }

    [Theory]
    [InlineData(nameof(SiteModel.Country), "IRL")]
    [InlineData(nameof(SiteModel.CurrencyCode), "€")]
    [InlineData(nameof(SiteModel.Locale), "xx-NOWHERE")]
    [InlineData(nameof(SiteModel.OperatorEmail), "not an address")]
    [InlineData(nameof(SiteModel.MailFromAddress), "@example.com")]
    public void ValuesThatAreNotWhatTheyClaimAreRefused(string property, string value)
    {
        var site = Valid();
        typeof(SiteModel).GetProperty(property)!.SetValue(site, value);

        SiteSettingsRules.WhyRefused(site).Should().NotBeNull();
    }

    [Fact]
    public void BlankAddressesAreAllowedBecauseBlankMeansNone()
    {
        var site = Valid();
        site.OperatorEmail = null;
        site.MailFromAddress = " ";

        SiteSettingsRules.WhyRefused(site).Should().BeNull();
    }

    // ---- The controller -----------------------------------------------------------------------

    private readonly ISiteData _sites = Substitute.For<ISiteData>();
    private readonly IUserData _users = Substitute.For<IUserData>();
    private readonly IAdminSiteContext _acting = Substitute.For<IAdminSiteContext>();

    private SiteController Controller(bool managesEveryStore)
    {
        _acting.SiteId.Returns(7);
        _sites.GetSettings(7).Returns(new SiteSettingsModel { Id = 7, SiteKey = "acme", Domain = "shop.example.com" });
        _users.GetUserById("admin-1").Returns(new List<UserModel> { new() { UserId = "admin-1", AllSites = managesEveryStore } });
        // A substituted string method answers "" by default, which here would read as a refusal.
        _sites.UpdateSettings(Arg.Any<SiteModel>()).Returns((string?)null);

        return new SiteController(_sites, _users)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "admin-1") }, "test"))
                }
            }
        };
    }

    [Fact]
    public void AnAdminOfOneStoreCannotMoveItToAnotherDomain()
    {
        var edited = Valid();
        edited.Domain = "elsewhere.example.com";

        var result = Controller(managesEveryStore: false)
            .UpdateSettings(edited, _acting, new MemoryCache(new MemoryCacheOptions()));

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        _sites.DidNotReceiveWithAnyArgs().UpdateSettings(default!);
    }

    [Fact]
    public void ASaveChangesTheActingStoresRowUnderItsOwnKeyWhateverTheBodySays()
    {
        var edited = Valid();
        edited.Domain = "shop.example.com";
        edited.Id = 99;
        edited.SiteKey = "someone-else";

        var result = Controller(managesEveryStore: false)
            .UpdateSettings(edited, _acting, new MemoryCache(new MemoryCacheOptions()));

        result.Should().BeOfType<NoContentResult>();
        _sites.Received(1).UpdateSettings(Arg.Is<SiteModel>(site => site.Id == 7 && site.SiteKey == "acme"));
    }

    [Fact]
    public void TheDatabasesRefusalReachesTheAdminAsWords()
    {
        var edited = Valid();
        edited.Domain = "shop.example.com";
        var controller = Controller(managesEveryStore: true);
        _sites.UpdateSettings(Arg.Any<SiteModel>()).Returns("The currency cannot change once the store has accounts.");

        var result = controller.UpdateSettings(edited, _acting, new MemoryCache(new MemoryCacheOptions()));

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be(
            "The currency cannot change once the store has accounts.");
    }
}
