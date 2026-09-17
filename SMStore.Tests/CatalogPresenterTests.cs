using FluentAssertions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Pricing;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Sites;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// CatalogPresenter is where a viewer turns into a price, or into the decision to show no
/// price at all. Pages never touch ICatalogData or IPriceResolver directly precisely so that
/// decision happens once — which makes this the one place it is worth getting right under
/// test, rather than trusting it falls out of testing every page that uses it.
/// </summary>
public class CatalogPresenterTests
{
    private readonly ICatalogData _catalog = Substitute.For<ICatalogData>();
    private readonly IPriceResolver _pricing = Substitute.For<IPriceResolver>();
    private readonly ICustomerContext _customer = Substitute.For<ICustomerContext>();

    private static SiteModel SiteWith(
        string priceDisplay = "Public", decimal minMarginPct = 0m, string currencyCode = "EUR", string locale = "en-IE") => new()
    {
        Id = 1,
        SiteKey = "test",
        Name = "Test store",
        Country = "IE",
        CurrencyCode = currencyCode,
        Locale = locale,
        PriceDisplay = priceDisplay,
        MinMarginPct = minMarginPct
    };

    private CatalogPresenter PresenterFor(SiteModel site)
    {
        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(site);
        siteContext.IsResolved.Returns(true);

        return new CatalogPresenter(_catalog, _pricing, siteContext, _customer);
    }

    // --- ShowPrices ---

    [Fact]
    public void HidesPricesFromAnAnonymousVisitorWhenTheSiteRequiresSignIn()
    {
        _customer.CustomerGroupId.Returns((int?)null);
        var presenter = PresenterFor(SiteWith(priceDisplay: "Authenticated"));

        presenter.ShowPrices.Should().BeFalse();
    }

    [Fact]
    public void ShowsPricesToASignedInCustomerEvenWhenTheSiteRequiresSignIn()
    {
        _customer.CustomerGroupId.Returns(7);
        var presenter = PresenterFor(SiteWith(priceDisplay: "Authenticated"));

        presenter.ShowPrices.Should().BeTrue();
    }

    [Fact]
    public void ShowsPricesToAnAnonymousVisitorOnAPublicSite()
    {
        _customer.CustomerGroupId.Returns((int?)null);
        var presenter = PresenterFor(SiteWith(priceDisplay: "Public"));

        presenter.ShowPrices.Should().BeTrue();
    }

    [Fact]
    public void MatchesAuthenticatedWithoutRegardToCase()
    {
        // PriceDisplay is hand-entered tenant configuration, the same reasoning
        // RegistrationFieldSetProvider applies to its own site-configured key.
        _customer.CustomerGroupId.Returns((int?)null);
        var presenter = PresenterFor(SiteWith(priceDisplay: "authenticated"));

        presenter.ShowPrices.Should().BeFalse();
    }

    // --- CustomerGroupId ---

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public void ReadsCustomerGroupIdFromTheResolvedSessionAndNothingElse(int? groupId)
    {
        _customer.CustomerGroupId.Returns(groupId);
        var presenter = PresenterFor(SiteWith());

        presenter.CustomerGroupId.Should().Be(groupId);
    }

    // --- Resolve ---

    [Fact]
    public void ResolvePassesTheCustomersDiscountAndTheSitesMarginFloor()
    {
        _customer.CustomerGroupId.Returns(4);
        _customer.GroupDiscountPct.Returns(12.5m);
        var site = SiteWith(minMarginPct: 8m);
        var presenter = PresenterFor(site);

        var item = new CatalogItemModel { RetailPrice = 100m, Cost = 60m };
        var expected = new ResolvedPrice { NetPrice = 87.5m };
        _pricing.Resolve(100m, 60m, 12.5m, 8m).Returns(expected);

        var resolved = presenter.Resolve(item);

        resolved.Should().BeSameAs(expected);
        _pricing.Received(1).Resolve(100m, 60m, groupDiscountPct: 12.5m, minMarginPct: 8m);
    }

    [Fact]
    public void ResolveDoesNotMixUpTheDiscountAndTheMarginFloor()
    {
        // Both are bare decimals on the same interface, so swapping the two arguments compiles
        // cleanly and prices every product on the site wrong. Pin the argument names, not just
        // that Resolve was called.
        _customer.CustomerGroupId.Returns(1);
        _customer.GroupDiscountPct.Returns(20m);
        var site = SiteWith(minMarginPct: 5m);
        var presenter = PresenterFor(site);
        var item = new CatalogItemModel { RetailPrice = 100m, Cost = 50m };

        presenter.Resolve(item);

        _pricing.Received(1).Resolve(100m, 50m, groupDiscountPct: 20m, minMarginPct: 5m);
        _pricing.DidNotReceive().Resolve(100m, 50m, groupDiscountPct: 5m, minMarginPct: 20m);
    }

    // --- Money ---

    [Fact]
    public void MoneyUsesTheSitesCurrencySymbolEvenWhenTheLocaleImpliesAnother()
    {
        var presenter = PresenterFor(SiteWith(currencyCode: "EUR", locale: "en-US"));

        var formatted = presenter.Money(1234.5m);

        formatted.Should().Contain("€");
        formatted.Should().NotContain("$");
    }

    [Fact]
    public void MoneySurvivesANonsenseLocaleWithoutThrowing()
    {
        var presenter = PresenterFor(SiteWith(currencyCode: "GBP", locale: "definitely-not-a-culture"));

        var act = () => presenter.Money(10m);

        act.Should().NotThrow();
        act().Should().Contain("£");
    }

    [Fact]
    public void MoneyFallsBackToTheLocalesOwnCurrencyWhenTheSiteSetsNone()
    {
        var presenter = PresenterFor(SiteWith(currencyCode: null!, locale: "en-US"));

        presenter.Money(10m).Should().Contain("$");
    }

    [Fact]
    public void MoneyFallsBackToTheCodeItselfForACurrencyWithNoKnownSymbol()
    {
        var presenter = PresenterFor(SiteWith(currencyCode: "PLN", locale: "en-IE"));

        presenter.Money(10m).Should().Contain("PLN");
    }

    // --- Search: the empty-page recovery ---

    [Fact]
    public void SearchRecoversFromAPagePastTheEndByLandingOnTheLastPage()
    {
        var presenter = PresenterFor(SiteWith());

        // Irrelevant to this test but on the path: ToCard prices every row it is given, and an
        // unconfigured NSubstitute member returns null for a reference type, not a dummy — so
        // landing on a real row without this would NullReferenceException inside FormatPrice.
        _pricing.Resolve(Arg.Any<decimal>(), Arg.Any<decimal?>(), Arg.Any<decimal>(), Arg.Any<decimal>())
            .Returns(new ResolvedPrice());

        // A page past the end comes back empty and with no total, since the total rides on the
        // rows. The presenter re-queries page 1 to recover the total, then re-queries again for
        // the actual last page — three calls in total for one bad deep link.
        _catalog.Search(Arg.Any<CatalogQuery>()).Returns(callInfo => callInfo.Arg<CatalogQuery>().Page switch
        {
            1 => new CatalogPage { TotalCount = 50, Page = 1, PageSize = 24 },
            3 => new CatalogPage { Items = [new CatalogItemModel { Sku = "LAST-PAGE" }], TotalCount = 50, Page = 3, PageSize = 24 },
            var page => new CatalogPage { TotalCount = 0, Page = page, PageSize = 24 }
        });
        _catalog.GetFacets(Arg.Any<CatalogQuery>()).Returns(new CatalogFacets());

        var result = presenter.Search(null, null, false, null, null, page: 9);

        result.Page.Should().Be(3);
        result.TotalPages.Should().Be(3);
        result.TotalCount.Should().Be(50);
        result.Items.Should().ContainSingle(item => item.Sku == "LAST-PAGE");
        _catalog.Received(3).Search(Arg.Any<CatalogQuery>());
        _catalog.Received(1).GetFacets(Arg.Any<CatalogQuery>());
    }

    [Fact]
    public void SearchDoesNotRetryWhenPageOneGenuinelyHasNoMatches()
    {
        var presenter = PresenterFor(SiteWith());

        _catalog.Search(Arg.Any<CatalogQuery>())
            .Returns(new CatalogPage { TotalCount = 0, Page = 1, PageSize = 24 });
        _catalog.GetFacets(Arg.Any<CatalogQuery>()).Returns(new CatalogFacets());

        var result = presenter.Search(null, null, false, "nothing matches this", null, page: 1);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
        _catalog.Received(1).Search(Arg.Any<CatalogQuery>());
    }

    // --- GetCategories ---

    [Fact]
    public void GetCategoriesExcludesInactiveOnes()
    {
        var presenter = PresenterFor(SiteWith());
        _catalog.GetCategories(1).Returns(
        [
            new SiteCategoryModel { Id = 1, Slug = "active", IsActive = true },
            new SiteCategoryModel { Id = 2, Slug = "retired", IsActive = false }
        ]);

        var categories = presenter.GetCategories();

        categories.Should().ContainSingle().Which.Slug.Should().Be("active");
    }
}
