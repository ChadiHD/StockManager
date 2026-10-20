using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using StockManager.ProductImages;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// Uploading and removing a product's photographs (T11): what reaches storage, in which order,
/// and what never does.
/// </summary>
public class ProductImageControllerTests
{
    private const int ActingSite = 42;
    private const string Stored = "0123456789abcdef0123456789abcdef.png";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3];

    private readonly IProductData _products = Substitute.For<IProductData>();
    private readonly IProductImageStore _images = Substitute.For<IProductImageStore>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly ProductController _controller;

    public ProductImageControllerTests()
    {
        _site.SiteId.Returns(ActingSite);
        _products.GetProductBySku(ActingSite, "SHIRT").Returns(new AdminProductModel { Id = 10, Sku = "SHIRT" });
        _products.GetProductBySku(ActingSite, "SHIRT-M").Returns(new AdminProductModel { Id = 11, Sku = "SHIRT-M", ParentProductId = 10 });
        _images.SaveAsync(default!, default!, default!).ReturnsForAnyArgs(Stored);
        _controller = new ProductController(_products);
    }

    private static IFormFile File(byte[] bytes, long? length = null) =>
        new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "file", "claims-to-be.jpg");

    [Fact]
    public async Task APhotographIsStoredAsWhatItsBytesSayThenRecorded()
    {
        var result = await _controller.AddImage("SHIRT", File(Png), _site, _images, default);

        result.Should().BeOfType<OkObjectResult>();
        // The filename said .jpg; the bytes say PNG, and the bytes are what count.
        await _images.Received(1).SaveAsync(Arg.Any<Stream>(), ".png", "image/png", Arg.Any<CancellationToken>());
        _products.Received(1).AddImage(10, Stored, "image/png");
    }

    [Fact]
    public async Task APdfIsRefusedBeforeAnythingIsStored()
    {
        var result = await _controller.AddImage("SHIRT", File(Encoding.ASCII.GetBytes("%PDF-1.7 not a photo")), _site, _images, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _images.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!);
    }

    [Fact]
    public async Task AnOversizedFileIsRefusedBeforeItIsRead()
    {
        var result = await _controller.AddImage("SHIRT", File(Png, ProductImageTypes.MaxBytes + 1), _site, _images, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _images.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!);
    }

    [Fact]
    public async Task AVariantHasNoPhotographsOfItsOwn()
    {
        var result = await _controller.AddImage("SHIRT-M", File(Png), _site, _images, default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AnotherStoresProductIsNotFound()
    {
        var result = await _controller.AddImage("SOMEONE-ELSES", File(Png), _site, _images, default);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task BytesWhoseRowFailedAreDeletedSoNothingIsLeftUnnamed()
    {
        _products.When(data => data.AddImage(10, Stored, "image/png")).Do(_ => throw new InvalidOperationException("row"));

        var act = () => _controller.AddImage("SHIRT", File(Png), _site, _images, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _images.Received(1).DeleteAsync(Stored, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyAPhotographThisProductHoldsIsDeleted()
    {
        _products.RemoveImage(10, Stored).Returns(false);

        (await _controller.RemoveImage("SHIRT", Stored, _site, _images, default)).Should().BeOfType<NotFoundResult>();
        (await _controller.RemoveImage("SHIRT", "../escape.png", _site, _images, default)).Should().BeOfType<NotFoundResult>();
        await _images.DidNotReceiveWithAnyArgs().DeleteAsync(default!);

        _products.RemoveImage(10, Stored).Returns(true);

        (await _controller.RemoveImage("SHIRT", Stored, _site, _images, default)).Should().BeOfType<NoContentResult>();
        await _images.Received(1).DeleteAsync(Stored, Arg.Any<CancellationToken>());
    }
}
