using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Pricing;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Components.Shared;
using SMStore.Ordering;
using SMStore.Registration;
using SMStore.Sites;
using SMStore.Tests.TestSupport;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// SMStore renders every tenant from one deployment and one set of components — that is the
/// entire reason the template exists, so it is worth testing directly rather than trusting it
/// falls out of the tests for each piece in isolation. Each test here renders the same
/// component twice, changing only the SiteModel (or what it drives), and checks the two
/// renders actually differ the way a second storefront needs them to.
/// </summary>
public class TenantVariationTests
{
    /// <summary>
    /// A second ordering mode standing in for the "DirectCheckout" mode the plan describes but
    /// has not built yet — enough to prove the header follows whichever mode is registered,
    /// without this test file inventing platform behaviour of its own.
    /// </summary>
    private sealed class FakeCheckoutOrderingMode : IOrderingMode
    {
        public string Key => "DirectCheckout";
        public string AddToBasketLabel => "Add to cart";
        public string BasketRoute => "/cart";
        public string BasketLabel => "Cart";
        public string SubmitBasketLabel => "Checkout";
        public bool RequiresApprovedAccount => true;
        public bool ShowsPayableTotal => true;
    }

    private static SiteModel SiteWith(string name, string orderMode) =>
        new() { Id = 1, SiteKey = "test", Name = name, OrderMode = orderMode };

    private static ISiteContext SiteContextReturning(SiteModel site)
    {
        var context = Substitute.For<ISiteContext>();
        context.Site.Returns(site);
        context.IsResolved.Returns(true);
        return context;
    }

    [Fact]
    public void TheHeaderBrandFollowsTheSiteName()
    {
        using var storeA = new Bunit.TestContext();
        var headerA = SiteHeaderHarness.Render(
            storeA, SiteWith("Acli Trade", RfqOrderingMode.ModeKey), new RfqOrderingMode());

        using var storeB = new Bunit.TestContext();
        var headerB = SiteHeaderHarness.Render(
            storeB, SiteWith("Widget Traders", RfqOrderingMode.ModeKey), new RfqOrderingMode());

        headerA.Find("a.site-header__brand img").GetAttribute("alt").Should().Be("Acli Trade");
        headerB.Find("a.site-header__brand img").GetAttribute("alt").Should().Be("Widget Traders");
    }

    [Fact]
    public void TheBasketControlFollowsTheSitesOrderingMode()
    {
        using var rfqStore = new Bunit.TestContext();
        var rfqHeader = SiteHeaderHarness.Render(
            rfqStore, SiteWith("Rfq Store", RfqOrderingMode.ModeKey), new RfqOrderingMode());

        using var checkoutStore = new Bunit.TestContext();
        var checkoutHeader = SiteHeaderHarness.Render(
            checkoutStore, SiteWith("Checkout Store", "DirectCheckout"), new FakeCheckoutOrderingMode());

        var rfqBasket = rfqHeader.Find("a.site-header__basket");
        rfqBasket.GetAttribute("href").Should().Be("/quote");
        rfqBasket.TextContent.Should().Contain("Quote");

        var checkoutBasket = checkoutHeader.Find("a.site-header__basket");
        checkoutBasket.GetAttribute("href").Should().Be("/cart");
        checkoutBasket.TextContent.Should().Contain("Cart");
    }

    [Fact]
    public void ProductPricingFollowsTheSitesPriceDisplayAndCurrency()
    {
        var catalog = Substitute.For<ICatalogData>();
        var pricing = Substitute.For<IPriceResolver>();
        pricing.Resolve(Arg.Any<decimal>(), Arg.Any<decimal?>(), Arg.Any<decimal>(), Arg.Any<decimal>())
            .Returns(callInfo => new ResolvedPrice { NetPrice = callInfo.ArgAt<decimal>(0) });

        var item = new CatalogItemModel { Sku = "ABC1", ProductName = "Widget", RetailPrice = 100m, Cost = 40m, QuantityInStock = 5 };
        var anonymousCustomer = Substitute.For<ICustomerContext>();

        var publicSite = new SiteModel
        {
            Id = 1, SiteKey = "public-site", Name = "Public Store",
            CurrencyCode = "EUR", Locale = "en-IE", PriceDisplay = "Public", OrderMode = RfqOrderingMode.ModeKey
        };
        var gatedSite = new SiteModel
        {
            Id = 2, SiteKey = "gated-site", Name = "Gated Store",
            CurrencyCode = "USD", Locale = "en-US", PriceDisplay = "Authenticated", OrderMode = RfqOrderingMode.ModeKey
        };

        var publicCard = new CatalogPresenter(catalog, pricing, SiteContextReturning(publicSite), anonymousCustomer).ToCard(item);
        var gatedCard = new CatalogPresenter(catalog, pricing, SiteContextReturning(gatedSite), anonymousCustomer).ToCard(item);

        // The presenter already diverges before a component is involved: the same anonymous
        // visitor, the same product, two different sites.
        publicCard.PriceLabel.Should().Contain("€");
        gatedCard.PriceLabel.Should().BeNull();

        using var publicStore = new Bunit.TestContext();
        publicStore.Services.AddSingleton(new OrderingModeProvider(SiteContextReturning(publicSite), [new RfqOrderingMode()]));
        var publicRender = publicStore.RenderComponent<ProductCard>(p => p.Add(x => x.Product, publicCard));

        using var gatedStore = new Bunit.TestContext();
        gatedStore.Services.AddSingleton(new OrderingModeProvider(SiteContextReturning(gatedSite), [new RfqOrderingMode()]));
        var gatedRender = gatedStore.RenderComponent<ProductCard>(p => p.Add(x => x.Product, gatedCard));

        publicRender.Find(".product-card__amount").TextContent.Should().Contain("€");
        gatedRender.FindAll(".product-card__amount").Should().BeEmpty();
        gatedRender.Find(".product-card__note").TextContent.Should().Be("Sign in to see pricing");
    }

    [Fact]
    public void RegistrationFieldsFollowTheSitesFieldSet()
    {
        using var euStore = new Bunit.TestContext();
        var euSite = new SiteModel { Id = 1, SiteKey = "eu-site", Name = "EU Store", Country = "IE", RegistrationFieldSet = "eu-b2b" };
        var euRender = RegisterPageHarness.Render(euStore, euSite, new EuB2bRegistrationFieldSet());

        using var minimalStore = new Bunit.TestContext();
        var minimalFieldSet = new FakeRegistrationFieldSet("minimal", new Dictionary<RegistrationField, RegistrationRequirement>
        {
            [RegistrationField.Email] = RegistrationRequirement.Required
        });
        var minimalSite = new SiteModel { Id = 2, SiteKey = "minimal-site", Name = "Minimal Store", Country = "IE", RegistrationFieldSet = "minimal" };
        var minimalRender = RegisterPageHarness.Render(minimalStore, minimalSite, minimalFieldSet);

        // eu-b2b asks for a VAT number and two supporting documents; the fake set asks for
        // neither -- the same page, wired to two different SiteModels, produces genuinely
        // different markup rather than the same form with different labels.
        euRender.Find("#reg-vatnumber");
        euRender.FindAll("input[type=file]").Should().HaveCount(2);

        minimalRender.FindAll("#reg-vatnumber").Should().BeEmpty();
        minimalRender.FindAll("input[type=file]").Should().BeEmpty();
        minimalRender.Find("#reg-email");
    }
}
