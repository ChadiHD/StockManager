using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin.Products;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

/// <summary>
/// A style's sizes and colours and its photographs, on the product screens (T11).
/// </summary>
public class ProductVariantTests : TestContext
{
    private readonly IAdminDataService _data = Substitute.For<IAdminDataService>();

    private static readonly Product Shirt = new()
    {
        Id = 10, Sku = "SHIRT", Name = "Linen shirt", Source = "Own", OnStore = true, Placement = "Mapped"
    };

    private static readonly Product Medium = new()
    {
        Id = 11, ParentId = 10, Sku = "SHIRT-M", Name = "Linen shirt — M / Navy", Source = "Own", Avail = 3
    };

    public ProductVariantTests()
    {
        Services.AddSingleton(Substitute.For<IToastService>());

        Product[] products = [Shirt, Medium];
        _data.Products.Returns(products);
        _data.GetProduct(Arg.Any<string>()).Returns(call => products.FirstOrDefault(p => p.Sku == call.Arg<string>()));
        _data.GetCategoryMappings().Returns(new CategoryMappingView([], []));
        _data.GetImages("SHIRT").Returns(Task.FromResult<IReadOnlyList<string>>(["0123456789abcdef0123456789abcdef.jpg"]));
        _data.GetVariants("SHIRT").Returns(Task.FromResult<IReadOnlyList<VariantItem>>([new VariantItem("SHIRT-M", "M / Navy", 3)]));

        Services.AddSingleton(_data);
    }

    [Fact]
    public void TheListShowsAStyleOnceWithACountOfItsVariants()
    {
        var cut = RenderComponent<Products>();

        // A shirt in six sizes is one row here, as it is one card in the shop.
        cut.Markup.Should().Contain("SHIRT").And.NotContain("SHIRT-M");
        cut.Find(".variant-count").TextContent.Should().Be("1 variant");
    }

    [Fact]
    public void AStylesPageListsItsVariantsAndPhotographs()
    {
        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SHIRT"));

        cut.Find(".variant-table").TextContent.Should().Contain("M / Navy").And.Contain("SHIRT-M");
        // From this origin's /img/, as the storefront serves it: an <img> carries no token.
        cut.Find(".image-strip img").GetAttribute("src").Should().Be("/img/0123456789abcdef0123456789abcdef.jpg");
    }

    [Fact]
    public void AddingAVariantSendsItsOptionsInTheOrderTyped()
    {
        _data.AddVariant(default!, default!, default, default!).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SHIRT"));

        cut.Find("#variant-sku").Change("SHIRT-L");
        cut.Find("#variant-stock").Change("4");
        cut.Find("#variant-name-1").Change("Size");
        cut.Find("#variant-value-1").Change("L");
        cut.Find("#variant-name-2").Change("Colour");
        cut.Find("#variant-value-2").Change("Navy");
        cut.Find(".variant-save").Click();

        _data.Received(1).AddVariant("SHIRT", "SHIRT-L", 4,
            Arg.Is<IReadOnlyList<(string Name, string Value)>>(options => SizeThenColour(options)));
    }

    // Outside the lambda: an expression tree cannot hold a tuple literal.
    private static bool SizeThenColour(IReadOnlyList<(string Name, string Value)> options) =>
        options.SequenceEqual(new[] { ("Size", "L"), ("Colour", "Navy") });

    [Fact]
    public void ARefusedVariantSaysWhy()
    {
        _data.AddVariant(default!, default!, default, default!)
            .ReturnsForAnyArgs("This product already has a variant with exactly those options.");
        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SHIRT"));

        cut.Find("#variant-sku").Change("SHIRT-M2");
        cut.Find("#variant-name-1").Change("Size");
        cut.Find("#variant-value-1").Change("M");
        cut.Find(".variant-save").Click();

        cut.Find(".variant-error").TextContent.Should().Contain("exactly those options");
    }

    [Fact]
    public void AVariantsPageOffersItsStockAndNothingItWouldNotKeep()
    {
        // spProduct_Update writes only a variant's stock; a form offering its price would accept
        // an edit and throw it away.
        var cut = RenderComponent<ProductDetail>(p => p.Add(x => x.Sku, "SHIRT-M"));

        cut.Find(".variant-edit").TextContent.Should().Contain("Linen shirt");
        cut.FindAll(".variant-edit input").Should().HaveCount(1);
        cut.FindAll(".product-variants").Should().BeEmpty();
        cut.FindAll(".product-images").Should().BeEmpty();
    }
}
