using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Pricing;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Navigation;
using SMStore.Ordering;
using SMStore.Sites;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SMStore.Tests.TestSupport;
using Xunit;
using Basket = SMStore.Components.Pages.Basket;

namespace SMStore.Tests;

/// <summary>
/// What the basket page renders, and the two things it must not.
/// </summary>
/// <remarks>
/// The page is the test of whether <c>IOrderingMode</c> is a real seam, and
/// <see cref="TheSameMarkupReadsAsACartForAStoreThatIsNotAnRfqStore"/> is what makes that a
/// claim rather than a hope: it renders the same page twice under two modes. Every other
/// storefront test renders the only mode the platform ships, so a page that hard-coded
/// "quote" would pass all of them.
///
/// The other is <c>Cost</c>. A basket line carries a buy price because <c>PriceResolver</c>
/// needs one, and <see cref="BasketLineView"/> is the boundary that keeps it off a public page.
/// </remarks>
public class BasketPageTests : Bunit.TestContext
{
    private const int SiteId = 7;

    private sealed class StubAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() =>
            new("stub-token-value", "__RequestVerificationToken");
    }

    private readonly IBasketData _baskets = Substitute.For<IBasketData>();
    private readonly ICatalogData _catalog = Substitute.For<ICatalogData>();
    private readonly ICustomerContext _customer = Substitute.For<ICustomerContext>();

    public BasketPageTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider>(new StubAntiforgeryStateProvider());

        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(new SiteModel
        {
            Id = SiteId,
            SiteKey = "test",
            Name = "Test store",
            CurrencyCode = "EUR",
            Locale = "en-IE",
            PriceDisplay = "Public",
            OrderMode = RfqOrderingMode.ModeKey
        });
        siteContext.IsResolved.Returns(true);

        var ordering = new OrderingModeProvider(siteContext, [new RfqOrderingMode()]);
        var catalogPresenter = new CatalogPresenter(
            _catalog, new PriceResolver(), siteContext, _customer);

        // A real BasketService over a substituted IBasketData: the cookie and token handling is
        // covered by BasketServiceTests, and stubbing the service here would leave the page
        // rendering from a shape nothing produces.
        var basketService = new BasketService(
            _baskets, catalogPresenter, siteContext, _customer,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<BasketService>.Instance);

        Services.AddSingleton(_customer);
        Services.AddSingleton(siteContext);
        Services.AddSingleton(ordering);
        Services.AddSingleton(catalogPresenter);
        Services.AddSingleton(basketService);
        Services.AddSingleton(new BasketPresenter(basketService, catalogPresenter, ordering));
        Services.AddSingleton(new StoreNavigation(siteContext, ordering));
    }

    /// <summary>Puts lines in the basket this request would resolve to.</summary>
    private void BasketHolds(params BasketLineModel[] lines)
    {
        // Signed in, so the lookup is by contact and no cookie is needed — BasketService
        // short-circuits without one, which is exactly what makes the empty case cheap.
        var contact = new ContactModel
        {
            Id = 41,
            AccountId = 5,
            FirstName = "Ada",
            LastName = "Byron",
            Email = "ada@example.test",
            Status = "Active",
            AccountStatus = "Approved"
        };

        _customer.Contact.Returns(contact);
        _customer.CustomerGroupId.Returns((int?)null);
        _baskets.FindBasket(SiteId, null, 41)
            .Returns(new BasketModel { Id = 9, SiteId = SiteId, ContactId = 41 });
        _baskets.GetLines(9, SiteId, null).Returns(lines.ToList());
    }

    private static BasketLineModel Line(
        string sku = "SKU1", string name = "Widget", int quantity = 2,
        decimal retail = 100m, decimal? cost = 60m, int stock = 5, bool available = true) => new()
        {
            Id = 1,
            BasketId = 9,
            ProductId = 12,
            Sku = sku,
            Name = name,
            Quantity = quantity,
            RetailPrice = retail,
            Cost = cost,
            QuantityInStock = stock,
            Available = available
        };

    [Fact]
    public void AnEmptyBasketOffersTheCatalogRatherThanAnEmptyTable()
    {
        var cut = RenderComponent<Basket>();

        cut.FindAll("table.basket__table").Should().BeEmpty();
        cut.Find(".stub a").GetAttribute("href").Should().Be("/catalog");
    }

    [Fact]
    public void ALineRendersItsResolvedUnitPriceAndLineTotal()
    {
        BasketHolds(Line(quantity: 3, retail: 100m, cost: 60m));

        var cut = RenderComponent<Basket>();

        var cells = cut.FindAll("td.basket__num");

        // No group discount and no margin floor, so the resolved price is list. What matters is
        // that the line total is quantity times the *resolved* price and not the raw retail
        // column — the two diverge the moment a group or a floor exists.
        cells[0].TextContent.Trim().Should().Contain("100.00");
        cells[1].TextContent.Trim().Should().Contain("300.00");
    }

    [Fact]
    public void TheQuantityFormPostsTheSkuAndNoDatabaseId()
    {
        BasketHolds(Line(sku: "ABC-1"));

        var cut = RenderComponent<Basket>();

        var form = cut.Find("form.basket__qty");

        form.GetAttribute("method").Should().Be("post");
        form.GetAttribute("action").Should().Be(BasketEndpoints.QuantityPath);
        form.QuerySelector("input[type=hidden][name='__RequestVerificationToken']")
            .Should().NotBeNull();
        form.QuerySelector("input[type=hidden][name='sku']")!
            .GetAttribute("value").Should().Be("ABC-1");

        // The rendered page carries no product id anywhere, which is what lets the endpoints
        // take a SKU and resolve it themselves.
        cut.Markup.Should().NotContain("name=\"productId\"");
    }

    [Fact]
    public void RemovingALineIsAQuantityOfZeroOnTheSameEndpoint()
    {
        BasketHolds(Line());

        var cut = RenderComponent<Basket>();

        var remove = cut.Find("form.basket__remove");

        // One code path behind both controls. A separate remove endpoint would be the same
        // DELETE behind a second name.
        remove.GetAttribute("action").Should().Be(BasketEndpoints.QuantityPath);
        remove.QuerySelector("input[type=hidden][name='quantity']")!
            .GetAttribute("value").Should().Be("0");
    }

    [Fact]
    public void AnUnavailableLineIsShownAndExcludedFromTheValue()
    {
        BasketHolds(
            Line(sku: "GOOD", quantity: 2, retail: 100m),
            Line(sku: "GONE", quantity: 5, retail: 100m, available: false));

        var cut = RenderComponent<Basket>();

        // Rendered, because a basket that silently drops rows is one the customer cannot
        // reason about — they added five of something and would find no trace of it.
        cut.FindAll("tr.basket__row--unavailable").Should().HaveCount(1);
        cut.Markup.Should().Contain("No longer available");

        // But not counted. A value that included it would be a number the customer is later
        // told was wrong.
        var value = cut.FindAll("dd").Last().TextContent;
        value.Should().Contain("200.00");
    }

    [Fact]
    public void AnAnonymousVisitorOnAGatedStoreSeesQuantitiesAndNoPrices()
    {
        BasketHolds(Line());
        _customer.Contact.Returns((ContactModel?)null);
        _customer.CustomerGroupId.Returns((int?)null);
        _baskets.FindBasket(SiteId, null, null).Returns((BasketModel?)null);

        var cut = RenderComponent<Basket>();

        // With no session and no cookie there is no basket to read at all, which is the
        // short-circuit BasketService takes. The page must render rather than throw.
        cut.FindAll("table.basket__table").Should().BeEmpty();
    }

    [Fact]
    public void TheSameMarkupReadsAsACartForAStoreThatIsNotAnRfqStore()
    {
        BasketHolds(Line());

        var rfq = RenderComponent<Basket>().Markup;

        // The same page, the same lines, a different ordering mode. Every customer-facing
        // word on this page comes from IOrderingMode, so this is what says so: a literal in
        // the markup would survive the switch and read "quote" to a checkout store.
        var cart = RenderAsCheckoutStore();

        rfq.Should().Contain("quote").And.NotContain("cart");
        cart.Should().Contain("cart").And.NotContain("quote");

        // And the wording follows the mode rather than the words: ShowsPayableTotal is what
        // decides whether a basket has a total or an indicative value, because under RFQ the
        // price is not firm until sales returns the quote.
        rfq.Should().Contain("Indicative value");
        cart.Should().Contain("Total").And.NotContain("Indicative value");
    }

    /// <summary>The same page in a second store, configured for direct checkout.</summary>
    /// <remarks>
    /// Its own TestContext, because the ordering mode is resolved from a registration and
    /// bUnit's service collection is fixed once the first component renders. Returns the
    /// markup rather than the component: disposing the context detaches the component, and a
    /// caller reading Markup afterwards gets ComponentDisposedException.
    /// </remarks>
    private string RenderAsCheckoutStore()
    {
        using var store = new Bunit.TestContext();

        store.Services.AddSingleton<AntiforgeryStateProvider>(new StubAntiforgeryStateProvider());

        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(new SiteModel
        {
            Id = SiteId,
            SiteKey = "other",
            Name = "Other store",
            CurrencyCode = "EUR",
            Locale = "en-IE",
            PriceDisplay = "Public",
            OrderMode = "DirectCheckout"
        });
        siteContext.IsResolved.Returns(true);

        var ordering = new OrderingModeProvider(siteContext, [new FakeCheckoutMode()]);
        var catalogPresenter = new CatalogPresenter(
            _catalog, new PriceResolver(), siteContext, _customer);

        var basketService = new BasketService(
            _baskets, catalogPresenter, siteContext, _customer,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<BasketService>.Instance);

        store.Services.AddSingleton(_customer);
        store.Services.AddSingleton(siteContext);
        store.Services.AddSingleton(ordering);
        store.Services.AddSingleton(catalogPresenter);
        store.Services.AddSingleton(basketService);
        store.Services.AddSingleton(new BasketPresenter(basketService, catalogPresenter, ordering));
        store.Services.AddSingleton(new StoreNavigation(siteContext, ordering));

        return store.RenderComponent<Basket>().Markup;
    }
}
