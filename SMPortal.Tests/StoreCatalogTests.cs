using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin.Products;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// The three screens through which an admin decides what one store sells: the product list's
// bulk actions, the product page's per-store panel, and the category mapping. What they share
// is the rule the placement function holds for the storefront -- the screens show its answer
// and send the admin's choice, and never work "on this store" out for themselves.
//
// And one regression that has nothing to do with stores and everything to do with this form:
// saving a product sent an empty description, IsTaxable = true and a category from a
// hardcoded list, so every price correction wiped the description, made an exempt product
// taxable and could take the product off every store.
public class StoreCatalogTests : TestContext
{
    public StoreCatalogTests()
    {
        Services.AddSingleton(Substitute.For<IToastService>());
    }

    private static Product Listed(string sku = "SKU-1") => new()
    {
        Sku = sku,
        Name = "E14 G2 i7/16GB",
        Desc = "LENOVO ThinkPad E14 G2",
        Cat = "Keyboards / Desktops",
        IsTaxable = false,
        Source = "Distributor",
        OnStore = true,
        Placement = "Mapped",
        StoreCategory = "Laptops",
    };

    private static Product NotListed(string sku = "SKU-2") => new()
    {
        Sku = sku,
        Name = "Unmapped part",
        Cat = "Tablet PC's",
        OnStore = false,
        Placement = "Unmapped",
    };

    private IAdminDataService Data(params Product[] products)
    {
        var data = Substitute.For<IAdminDataService>();

        // NSubstitute hands back null for an unconfigured list, which the real service never does.
        data.Products.Returns(products);
        data.GetProduct(Arg.Any<string>()).Returns(call => products.FirstOrDefault(p => p.Sku == call.Arg<string>()));
        data.GetCategoryMappings().Returns(new CategoryMappingView(
            [new StoreCategoryOption(7, "Laptops", true), new StoreCategoryOption(8, "Retired", false)],
            [new FeedCategoryRow("Keyboards / Desktops", 12, 7, "Laptops"), new FeedCategoryRow("Tablet PC's", 4, null, null)]));

        Services.AddSingleton(data);

        return data;
    }

    // --- Product page ---------------------------------------------------------------------

    [Fact]
    public void TheProductFormNoLongerOffersToRewriteTheFeedCategory()
    {
        Data(Listed());

        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SKU-1"));

        // The hardcoded seven are gone, and the feed's own category is shown instead.
        cut.Markup.Should().NotContain("<option>Servers</option>");
        cut.Find(".feed-category").TextContent.Should().Be("Keyboards / Desktops");
    }

    [Fact]
    public void SavingTheProductSendsBackWhatTheFormDoesNotEdit()
    {
        var data = Data(Listed());
        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SKU-1"));

        cut.Find("form").Submit();

        data.Received(1).UpdateProduct(Arg.Is<Product>(edited =>
            edited.Desc == "LENOVO ThinkPad E14 G2"
            && edited.Cat == "Keyboards / Desktops"
            && edited.IsTaxable == false));
    }

    [Fact]
    public void ThePanelSaysWhereTheProductSitsOnThisStore()
    {
        Data(Listed());

        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SKU-1"));

        cut.Find(".placement-status").TextContent.Should().Contain("Listed").And.Contain("Laptops");
    }

    [Fact]
    public void HidingAProductSendsTheChoiceForThisStore()
    {
        var data = Data(Listed());
        data.SetPlacement(default!, default, default, default, default).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SKU-1"));

        cut.Find("#placement-visibility").Change("Hide");
        cut.Find("button.placement-save").Click();

        data.Received(1).SetPlacement("SKU-1", "Hide", null, false, null);
    }

    [Fact]
    public void ShowingAProductOffersOnlyTheStoresActiveCategories()
    {
        var data = Data(NotListed());
        data.SetPlacement(default!, default, default, default, default).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SKU-2"));

        cut.Find("#placement-visibility").Change("Show");

        var options = cut.FindAll("#placement-category option").Select(o => o.TextContent).ToList();
        options.Should().Contain("Laptops").And.NotContain("Retired");

        cut.Find("#placement-category").Change("7");
        cut.Find("button.placement-save").Click();

        data.Received(1).SetPlacement("SKU-2", "Show", 7, false, null);
    }

    [Fact]
    public void ARefusalIsShownBesideTheForm()
    {
        var data = Data(NotListed());
        data.SetPlacement(default!, default, default, default, default)
            .ReturnsForAnyArgs("Choose a category to show this product under: its feed category is not mapped on this store.");
        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SKU-2"));

        cut.Find("#placement-visibility").Change("Show");
        cut.Find("button.placement-save").Click();

        cut.Find(".placement-error").TextContent.Should().Contain("not mapped on this store");
    }

    // --- Product list ---------------------------------------------------------------------

    [Fact]
    public void TheListShowsEachProductsPlaceOnThisStore()
    {
        Data(Listed(), NotListed());

        var cut = RenderComponent<Products>();

        var cells = cut.FindAll("td.placement-cell").Select(cell => cell.TextContent).ToList();
        cells.Should().Contain(text => text.Contains("Laptops"));
        cells.Should().Contain(text => text.Contains("not mapped") && text.Contains("Tablet PC's"));
    }

    [Fact]
    public void ASelectionCanBeHiddenFromThisStoreInOneGo()
    {
        var data = Data(Listed("SKU-1"), Listed("SKU-3"));
        data.SetVisibility(default!, default).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<Products>();

        cut.Find("input[aria-label='Select SKU-1']").Change(true);
        cut.Find("input[aria-label='Select SKU-3']").Change(true);
        cut.Find("button.bulk-hide").Click();

        data.Received(1).SetVisibility(
            Arg.Is<IReadOnlyCollection<string>>(skus => skus.OrderBy(s => s).SequenceEqual(new[] { "SKU-1", "SKU-3" })),
            "Hide");
    }

    // --- Category mapping -----------------------------------------------------------------

    [Fact]
    public void MappingAFeedCategorySendsTheStoresCategory()
    {
        var data = Data();
        data.MapCategory(default!, default).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<Categories>();

        cut.FindAll("select.mapping-select")[1].Change("7");

        data.Received(1).MapCategory("Tablet PC's", 7);
    }

    [Fact]
    public void ChoosingNotSoldHereUnmapsIt()
    {
        var data = Data();
        data.MapCategory(default!, default).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<Categories>();

        cut.FindAll("select.mapping-select")[0].Change("");

        data.Received(1).MapCategory("Keyboards / Desktops", null);
    }

    [Fact]
    public void AnInactiveCategoryIsNotOfferedForANewMapping()
    {
        Data();
        var cut = RenderComponent<Categories>();

        // Still shown where it is already in use, so the select reflects what is stored.
        cut.FindAll("select.mapping-select")[1].TextContent.Should().NotContain("Retired");
    }

    // --- The store's own categories (T9) -------------------------------------------------------

    [Fact]
    public void TheStoresOwnCategoriesAreListedAndANewOneCanBeAdded()
    {
        // Before T9 the screen mapped feed categories onto store categories nobody could create.
        var data = Data();
        data.SaveStoreCategory(default!).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<Categories>();

        cut.FindAll(".store-category-row").Should().HaveCount(2);

        cut.Find("button.add-category").Click();
        cut.Find("#category-name").Change("Networking Kit & Cables");
        cut.Find("button.category-save").Click();

        // The address follows the name until somebody types one.
        data.Received(1).SaveStoreCategory(Arg.Is<StoreCategoryOption>(c =>
            c.Id == 0 && c.Name == "Networking Kit & Cables" && c.Slug == "networking-kit-cables" && c.IsActive));
    }

    [Fact]
    public void ARefusedCategoryKeepsTheEditorOpenWithTheReason()
    {
        var data = Data();
        data.SaveStoreCategory(default!).ReturnsForAnyArgs(
            "The address cannot change while the store is live: customers have it bookmarked.");
        var cut = RenderComponent<Categories>();

        cut.FindAll(".store-category-row button")[0].Click();
        cut.Find("#category-slug").Change("laptops-new");
        cut.Find("button.category-save").Click();

        data.Received(1).SaveStoreCategory(Arg.Is<StoreCategoryOption>(c => c.Id == 7 && c.Slug == "laptops-new"));
        cut.Find(".category-error").TextContent.Should().Contain("customers have it bookmarked");
    }
}
