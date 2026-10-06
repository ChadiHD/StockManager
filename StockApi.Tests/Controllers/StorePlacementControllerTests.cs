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
/// The admin's per-store product choices and category mapping, at the boundary.
/// </summary>
/// <remarks>
/// What the procedures refuse is <c>StorePlacementTests</c>' to say, against the database.
/// What is worth asserting here is that every call carries the store the admin is acting for —
/// a placement written without it would be one store's choice landing on another — and that a
/// refusal reaches the portal as a sentence the admin can act on rather than as a 500.
/// </remarks>
public class StorePlacementControllerTests
{
    private const int ActingSite = 42;

    private readonly IProductData _products = Substitute.For<IProductData>();
    private readonly ICategoryMappingData _mappings = Substitute.For<ICategoryMappingData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly ProductController _productController;
    private readonly CategoryMappingController _mappingController;

    public StorePlacementControllerTests()
    {
        _site.SiteId.Returns(ActingSite);
        _products.GetProductBySku("SKU-1").Returns(new AdminProductModel { Id = 1, Sku = "SKU-1" });

        _productController = new ProductController(_products);
        _mappingController = new CategoryMappingController(_mappings, _site);
    }

    [Fact]
    public void TheCatalogIsReadForTheActingStore()
    {
        _productController.GetCatalog(_site);

        _products.Received(1).GetCatalog(ActingSite);
    }

    [Fact]
    public void APlacementIsWrittenForTheActingStore()
    {
        _products.SetPlacement(default, default!, default!, default, default, default!)
            .ReturnsForAnyArgs(new PlacementResult(1, null!));

        var result = _productController.SetPlacement(
            "SKU-1", new ProductController.PlacementModel("Show", 7, true, "New"), _site);

        result.Should().BeOfType<NoContentResult>();
        _products.Received(1).SetPlacement(ActingSite, "SKU-1", "Show", 7, true, "New");
    }

    [Theory]
    [InlineData("show")]
    [InlineData("Visible")]
    public void AVisibilityThatIsNotOneOfTheThreeIsRefusedBeforeTheDatabase(string visibility)
    {
        // CK_SiteProduct_Visibility would refuse it too, from inside a procedure, as a 500.
        var result = _productController.SetPlacement(
            "SKU-1", new ProductController.PlacementModel(visibility, null, false, null), _site);

        result.Should().BeOfType<BadRequestObjectResult>();
        _products.DidNotReceiveWithAnyArgs().SetPlacement(default, default!, default!, default, default, default!);
    }

    [Fact]
    public void AnUnknownSkuIsNotFound()
    {
        var result = _productController.SetPlacement(
            "NO-SUCH", new ProductController.PlacementModel("Hide", null, false, null), _site);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void ARefusalReachesTheAdminAsTheProceduresOwnSentence()
    {
        const string refusal = "Choose a category to show this product under: its feed category is not mapped on this store.";
        _products.SetPlacement(default, default!, default!, default, default, default!)
            .ReturnsForAnyArgs(new PlacementResult(0, refusal));

        var result = _productController.SetPlacement(
            "SKU-1", new ProductController.PlacementModel("Show", null, false, null), _site);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be(refusal);
    }

    [Fact]
    public void ABulkChangeReportsHowManyProductsItReached()
    {
        _products.SetVisibility(default, default!, default!, default)
            .ReturnsForAnyArgs(new PlacementResult(2, null!));

        var result = _productController.SetVisibility(
            new ProductController.BulkVisibilityModel(["SKU-1", "SKU-2"], "Hide", null), _site);

        result.Value.Should().Be(2);
        _products.Received(1).SetVisibility(
            ActingSite, Arg.Is<IEnumerable<string>>(skus => skus.SequenceEqual(new[] { "SKU-1", "SKU-2" })),
            "Hide", null);
    }

    [Fact]
    public void AnEmptyOrOversizedSelectionIsRefused()
    {
        _productController.SetVisibility(new ProductController.BulkVisibilityModel([], "Hide", null), _site)
            .Result.Should().BeOfType<BadRequestObjectResult>();

        var tooMany = Enumerable.Range(0, 1001).Select(i => $"SKU-{i}").ToList();

        _productController.SetVisibility(new ProductController.BulkVisibilityModel(tooMany, "Hide", null), _site)
            .Result.Should().BeOfType<BadRequestObjectResult>();

        _products.DidNotReceiveWithAnyArgs().SetVisibility(default, default!, default!, default);
    }

    [Fact]
    public void TheMappingScreenIsReadForTheActingStore()
    {
        _mappingController.Get();

        _mappings.Received(1).GetSiteCategories(ActingSite);
        _mappings.Received(1).GetFeedCategories(ActingSite);
    }

    [Fact]
    public void AMappingIsWrittenForTheActingStore_AndARefusalIsSaid()
    {
        _mappings.Map(ActingSite, "Keyboards / Desktops", 7).Returns((string)null!);
        _mappings.Map(ActingSite, "Keyboards / Desktops", 99).Returns("That category is not one of this store's active categories.");

        _mappingController.Put(new CategoryMappingController.MappingModel("Keyboards / Desktops", 7))
            .Should().BeOfType<NoContentResult>();

        _mappingController.Put(new CategoryMappingController.MappingModel("Keyboards / Desktops", 99))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void AMappingWithNoFeedCategoryIsRefused()
    {
        _mappingController.Put(new CategoryMappingController.MappingModel(" ", 7))
            .Should().BeOfType<BadRequestObjectResult>();

        _mappings.DidNotReceiveWithAnyArgs().Map(default, default!, default);
    }

    // --- The store's own categories (T9) -------------------------------------------------------

    [Theory]
    [InlineData("Laptops & Bags")]
    [InlineData("laptops--bags")]
    [InlineData("-laptops")]
    [InlineData("")]
    public void ACategoryAddressIsOnlyWhatReadsCleanlyInALink(string slug)
    {
        // It is the cat= value on every catalog link, so nothing that needs escaping.
        _mappingController.CreateCategory(new CategoryMappingController.CategoryModel(slug, "Laptops", null, 0, true))
            .Should().BeOfType<BadRequestObjectResult>();

        _mappings.DidNotReceiveWithAnyArgs().SaveCategory(default, default!);
    }

    [Fact]
    public void ACategoryIsSavedForTheActingStore()
    {
        _mappings.SaveCategory(default, default!).ReturnsForAnyArgs((12, (string)null!));

        _mappingController.UpdateCategory(12, new CategoryMappingController.CategoryModel(" Laptops-Pro ", " Laptops ", null, 3, false))
            .Should().BeOfType<OkObjectResult>();

        _mappings.Received(1).SaveCategory(ActingSite, Arg.Is<SiteCategoryModel>(c =>
            c.Id == 12 && c.Slug == "laptops-pro" && c.Name == "Laptops" && c.SortOrder == 3 && !c.IsActive));
    }
}
