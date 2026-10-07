using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Pricing;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Content;
using SMStore.Ordering;
using SMStore.Sites;
using Xunit;
using Home = SMStore.Components.Pages.Home;

namespace SMStore.Tests;

/// <summary>
/// The home page is the store's own (T9). Until then every store rendered a placeholder saying
/// the hero copy, category grid and featured products "arrive" later.
/// </summary>
public class HomePageTests : TestContext
{
    private readonly ISiteContentSource _content = Substitute.For<ISiteContentSource>();
    private readonly ICatalogData _catalog = Substitute.For<ICatalogData>();

    public HomePageTests()
    {
        var site = new SiteModel
        {
            Id = 3, SiteKey = "acme", Name = "Acme Trade", CurrencyCode = "EUR", Locale = "en-IE",
            PriceDisplay = "Public", OrderMode = RfqOrderingMode.ModeKey
        };
        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(site);
        siteContext.IsResolved.Returns(true);

        var pricing = Substitute.For<IPriceResolver>();
        pricing.Resolve(Arg.Any<decimal>(), Arg.Any<decimal?>(), Arg.Any<decimal>(), Arg.Any<decimal>())
            .Returns(call => new ResolvedPrice { NetPrice = call.ArgAt<decimal>(0) });

        _catalog.GetCategories(3).Returns(new List<SiteCategoryModel>());
        _catalog.Search(Arg.Any<CatalogQuery>()).Returns(new CatalogPage { Items = new List<CatalogItemModel>() });

        Services.AddSingleton(siteContext);
        Services.AddSingleton(_content);
        Services.AddSingleton(new CatalogPresenter(_catalog, pricing, siteContext, Substitute.For<ICustomerContext>()));
        Services.AddSingleton(new OrderingModeProvider(siteContext, [new RfqOrderingMode()]));
    }

    [Fact]
    public void TheHeroIsTheStoresOwnHomeContent()
    {
        _content.GetAsync(3, "home", Arg.Any<CancellationToken>())
            .Returns(new SiteContentPage("home", "Trade IT, delivered", "Next-day across Ireland.", "<p>Since 1998.</p>"));

        var cut = RenderComponent<Home>();

        cut.Find("h1").TextContent.Should().Be("Trade IT, delivered");
        cut.Find(".page-lede").TextContent.Should().Be("Next-day across Ireland.");
        cut.Find(".home-body").InnerHtml.Should().Be("<p>Since 1998.</p>");
    }

    [Fact]
    public void AStoreWithNoHomeContentGetsItsNameAndNoPlaceholder()
    {
        _content.GetAsync(3, "home", Arg.Any<CancellationToken>()).Returns((SiteContentPage?)null);

        var cut = RenderComponent<Home>();

        cut.Find("h1").TextContent.Should().Be("Acme Trade");
        cut.Markup.Should().NotContain("arrive").And.NotContain("stub").And.NotContain("land with T2");
    }

    [Fact]
    public void TheStoresActiveCategoriesLinkIntoTheCatalogWithTheirBlurbs()
    {
        _catalog.GetCategories(3).Returns(new List<SiteCategoryModel>
        {
            new() { Id = 1, SiteId = 3, Slug = "laptops", Name = "Laptops", Blurb = "Business notebooks", IsActive = true },
            new() { Id = 2, SiteId = 3, Slug = "retired", Name = "Retired", IsActive = false }
        });

        var cut = RenderComponent<Home>();

        var categories = cut.FindAll("a.home-category");
        categories.Should().ContainSingle();
        categories[0].GetAttribute("href").Should().Be("/catalog?cat=laptops");
        categories[0].TextContent.Should().Contain("Business notebooks");
    }

    [Fact]
    public void TheFeaturedStripIsTheTopOfTheCatalogsDefaultOrderWithoutFacets()
    {
        _catalog.Search(Arg.Any<CatalogQuery>()).Returns(new CatalogPage
        {
            Items = new List<CatalogItemModel> { new() { Sku = "SKU-1", ProductName = "Featured laptop", RetailPrice = 900m } }
        });

        var cut = RenderComponent<Home>();

        cut.FindAll(".home-featured .product-card").Should().ContainSingle();
        _catalog.Received(1).Search(Arg.Is<CatalogQuery>(query => query.PageSize == 8 && query.Page == 1));
        _catalog.DidNotReceiveWithAnyArgs().GetFacets(default!);
    }
}
