using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin.Products;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// The products list as an admin works through it: a search that outlives a visit to a product,
// each row's own photograph, and a delisted line that says so rather than "Out of stock".
public class ProductListTests : TestContext
{
    public ProductListTests()
    {
        Services.AddSingleton(Substitute.For<IToastService>());
        Services.AddSingleton(new ProductListState());
    }

    private static Product Item(string sku, string desc, int avail = 20) => new()
    {
        Sku = sku, Name = $"{desc} spec", Desc = desc, Avail = avail, OnStore = true, Placement = "Mapped"
    };

    private void Data(params Product[] products)
    {
        var data = Substitute.For<IAdminDataService>();
        data.Products.Returns(products);
        Services.AddSingleton(data);
    }

    private static IElement Row(IRenderedFragment cut, string sku) =>
        cut.FindAll("tbody tr").Single(row => row.TextContent.Contains(sku));

    [Fact]
    public void ASearchIsStillAppliedAfterOpeningAProductAndComingBack()
    {
        Data(Item("SKU-1", "LENOVO ThinkPad E14"), Item("SKU-2", "HP EliteBook 840"));
        var cut = RenderComponent<Products>();

        cut.Find("input[type=search]").Input("EliteBook");
        cut.Find("button.searchbox-go").Click();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle());

        // Opening a product navigates away, and the page and its fields go with it.
        DisposeComponents();
        var back = RenderComponent<Products>();

        back.Find("input[type=search]").GetAttribute("value").Should().Be("EliteBook");
        back.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("SKU-2");
    }

    [Fact]
    public void EachRowShowsItsOwnPhotographOrSaysItHasNone()
    {
        var pictured = Item("SKU-1", "Pictured");
        pictured.Image = "/img/0123456789abcdef0123456789abcdef.png";
        Data(pictured, Item("SKU-2", "Bare"));

        var cut = RenderComponent<Products>();

        Row(cut, "SKU-1").QuerySelector("td.thumb-cell img")!.GetAttribute("src")
            .Should().Be("/img/0123456789abcdef0123456789abcdef.png");
        Row(cut, "SKU-2").QuerySelector("td.thumb-cell [aria-label='No image']").Should().NotBeNull();
        cut.FindAll("td.thumb-cell .tile").Should().BeEmpty("a category monogram is not a picture of the product");
    }

    [Fact]
    public void ADelistedLineSaysSoAndIsNotLowStock()
    {
        // The feed import zeroes a delisted line's stock and keeps the row, and every one of them
        // read "0 · Out of stock".
        var dropped = Item("SKU-1", "Dropped by the distributor", avail: 0);
        dropped.Source = "Distributor";
        dropped.Delisted = true;
        Data(dropped, Item("SKU-2", "Nearly gone", avail: 2));

        var cut = RenderComponent<Products>();

        Row(cut, "SKU-1").QuerySelector("td.trail .badge")!.TextContent.Should().Be("Delisted");
        Row(cut, "SKU-2").QuerySelector("td.trail .badge")!.TextContent.Should().Be("2 · Low");

        cut.FindAll("button.pill").Single(pill => pill.TextContent == "Low stock").Click();

        cut.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("SKU-2");
    }
}
