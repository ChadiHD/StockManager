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

    [Theory]
    [InlineData(7)]
    // The case this used to get wrong. A pricing group is optional, and an approved customer
    // nobody has grouped yet is still a registered customer: they see list price.
    [InlineData(null)]
    public void ShowsPricesToASignedInCustomerWhenTheSiteRequiresSignIn(int? groupId)
    {
        _customer.IsSignedIn.Returns(true);
        _customer.CustomerGroupId.Returns(groupId);
        var presenter = PresenterFor(SiteWith(priceDisplay: "Authenticated"));

        presenter.ShowPrices.Should().BeTrue();
    }

    [Theory]
    [InlineData("price-asc")]
    [InlineData("price-desc")]
    [InlineData("PRICE-ASC")]
    public void APriceSortIsDroppedForAViewerWhoMayNotSeePrices(string sort)
    {
        // The cards hide the amount, but the order alone ranks the prices — and ?sort= is a
        // query string anybody can write, so hiding the link would not be enough.
        _customer.IsSignedIn.Returns(false);
        var presenter = PresenterFor(SiteWith(priceDisplay: "Authenticated"));

        presenter.SortFor(sort).Should().BeNull();
        presenter.SortFor("name").Should().Be("name");

        _catalog.Search(Arg.Any<CatalogQuery>()).Returns(new CatalogPage { Page = 1, PageSize = 24 });
        _catalog.GetFacets(Arg.Any<CatalogQuery>()).Returns(new CatalogFacets());
        presenter.Search(null, null, false, null, sort, 1);

        _catalog.Received(1).Search(Arg.Is<CatalogQuery>(query => query.Sort == null));
    }

    [Fact]
    public void APriceSortIsKeptForAViewerWhoMaySeePrices()
    {
        _customer.IsSignedIn.Returns(false);
        var presenter = PresenterFor(SiteWith(priceDisplay: "Public"));

        presenter.SortFor("price-asc").Should().Be("price-asc");
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

    // --- Product page (T11) ---

    private void FamilyOfAShirt()
    {
        var style = new CatalogItemModel { Id = 10, Sku = "SHIRT", ProductName = "Linen shirt", HasVariants = true, QuantityInStock = 3 };
        var medium = new CatalogItemModel { Id = 11, Sku = "SHIRT-M", ParentProductId = 10, QuantityInStock = 3 };
        var large = new CatalogItemModel { Id = 12, Sku = "SHIRT-L", ParentProductId = 10, QuantityInStock = 0 };

        _catalog.GetBySku(1, "SHIRT", Arg.Any<int?>()).Returns(style);
        _catalog.GetBySku(1, "SHIRT-L", Arg.Any<int?>()).Returns(large);
        _catalog.GetFamily(1, 10, Arg.Any<int?>()).Returns(new CatalogFamily
        {
            Style = style,
            Variants = [medium, large],
            Options =
            [
                // Out of order on purpose: the label follows SortOrder, as the variant's name does.
                new ProductOptionModel { ProductId = 11, Name = "Colour", Value = "Navy", SortOrder = 1 },
                new ProductOptionModel { ProductId = 11, Name = "Size", Value = "M", SortOrder = 0 },
                new ProductOptionModel { ProductId = 12, Name = "Size", Value = "L", SortOrder = 0 },
                new ProductOptionModel { ProductId = 12, Name = "Colour", Value = "Navy", SortOrder = 1 }
            ]
        });
    }

    [Fact]
    public void AStylesPageOffersEveryVariantByItsOptions()
    {
        FamilyOfAShirt();

        var page = PresenterFor(SiteWith()).GetProductPage("SHIRT")!;

        page.Product.Sku.Should().Be("SHIRT");
        page.ChoiceLabel.Should().Be("Size / Colour");
        page.Variants.Should().Equal(
            new VariantChoice("SHIRT-M", "M / Navy", true),
            new VariantChoice("SHIRT-L", "L / Navy", false));
        page.SelectedSku.Should().BeNull();
    }

    [Fact]
    public void AVariantsSkuOpensItsStylesPageWithThatVariantChosen()
    {
        // A basket line or a quote links to the variant it holds, and the page that opens is
        // the one where the size can be changed.
        FamilyOfAShirt();

        var page = PresenterFor(SiteWith()).GetProductPage("SHIRT-L")!;

        page.Product.Sku.Should().Be("SHIRT");
        page.SelectedSku.Should().Be("SHIRT-L");
    }

    [Fact]
    public void AVariantOfAStyleThatIsNotOnSaleHasNoPage()
    {
        FamilyOfAShirt();
        _catalog.GetFamily(1, 10, Arg.Any<int?>()).Returns(new CatalogFamily());

        PresenterFor(SiteWith()).GetProductPage("SHIRT-L").Should().BeNull();
    }

    // --- Option filter (T11) ---

    [Fact]
    public void AnOptionFilterFromTheUrlKeepsOnlyWhatTheQueryCanTake()
    {
        // Each of these would otherwise reach a table-valued parameter keyed on the pair, with
        // 50-character columns, and fail there as a 500.
        var parsed = CatalogPresenter.ParseOptions(
        [
            "Size:M",
            "Size:M",
            " Colour : Navy ",
            "no-colon",
            ":M",
            "Size:",
            null,
            $"Size:{new string('x', 51)}",
            "Note:a:b"
        ]);

        parsed.Should().Equal(
            new CatalogOption("Size", "M"),
            new CatalogOption("Colour", "Navy"),
            new CatalogOption("Note", "a:b"));
    }

    [Fact]
    public void AnOptionFilterIsCapped()
    {
        CatalogPresenter.ParseOptions(Enumerable.Range(0, 50).Select(i => $"Size:{i}"))
            .Should().HaveCount(20);
    }

    [Fact]
    public void AnOptionRoundTripsThroughItsUrlForm()
    {
        var option = new CatalogOption("Colour", "Navy");

        CatalogPresenter.ParseOptions([CatalogPresenter.Encode(option)]).Should().Equal(option);
    }
}
