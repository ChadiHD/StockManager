using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// Adding and removing a style's variants (T11), at the boundary. What the procedures refuse is
/// <c>VariantCatalogTests</c>' to say against the database; here, that the acting store reaches
/// them and that a refusal reaches the admin as a sentence.
/// </summary>
public class VariantControllerTests
{
    private const int ActingSite = 42;

    private readonly IProductData _products = Substitute.For<IProductData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly ProductController _controller;

    public VariantControllerTests()
    {
        _site.SiteId.Returns(ActingSite);
        _products.GetProductBySku(ActingSite, "SHIRT").Returns(new AdminProductModel { Id = 10, Sku = "SHIRT" });
        _controller = new ProductController(_products);
    }

    private static ProductController.NewVariantModel Medium(string sku = "SHIRT-M") =>
        new(sku, 3, [new("Size", "M"), new("Colour", "Navy")]);

    [Fact]
    public void AVariantIsAddedToTheStyleForTheActingStoreWithItsOptionsInOrder()
    {
        _products.AddVariant(default, default, default!, default, default!).ReturnsForAnyArgs(new VariantResult(11, null!));

        _controller.AddVariant("SHIRT", Medium(), _site);

        _products.Received(1).AddVariant(ActingSite, 10, "SHIRT-M", 3,
            Arg.Is<IEnumerable<(string Name, string Value)>>(options => SizeThenColour(options)));
    }

    // Outside the lambda: an expression tree cannot hold a tuple literal.
    private static bool SizeThenColour(IEnumerable<(string Name, string Value)> options) =>
        options.SequenceEqual(new[] { ("Size", "M"), ("Colour", "Navy") });

    [Fact]
    public void AStyleThisStoreDoesNotSellIsNotFound()
    {
        _controller.AddVariant("SOMEONE-ELSES", Medium(), _site).Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void ASkuAlreadyInUseIsAConflict()
    {
        _products.GetProductBySku(ActingSite, "SHIRT-M").Returns(new AdminProductModel { Id = 11, Sku = "SHIRT-M" });

        _controller.AddVariant("SHIRT", Medium(), _site).Result.Should().BeOfType<ConflictObjectResult>();
        _products.DidNotReceiveWithAnyArgs().AddVariant(default, default, default!, default, default!);
    }

    [Fact]
    public void AProcedureRefusalReachesTheAdminAsItsOwnSentence()
    {
        _products.AddVariant(default, default, default!, default, default!)
            .ReturnsForAnyArgs(new VariantResult(null, "This product already has a variant with exactly those options."));

        var result = _controller.AddVariant("SHIRT", Medium(), _site).Result;

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("This product already has a variant with exactly those options.");
    }

    [Fact]
    public void OnlyAVariantCanBeRemovedThisWay()
    {
        // The style itself has no parent: removing products in general is not what this does.
        _controller.RemoveVariant("SHIRT", _site).Should().BeOfType<NotFoundResult>();
        _products.DidNotReceiveWithAnyArgs().RemoveVariant(default, default);
    }

    [Fact]
    public void AVariantSomebodyHoldsIsRefusedWithTheReason()
    {
        _products.GetProductBySku(ActingSite, "SHIRT-M")
            .Returns(new AdminProductModel { Id = 11, Sku = "SHIRT-M", ParentProductId = 10 });
        _products.RemoveVariant(ActingSite, 11).Returns("A basket, quote or order holds this variant.");

        _controller.RemoveVariant("SHIRT-M", _site).Should().BeOfType<BadRequestObjectResult>();
    }
}
