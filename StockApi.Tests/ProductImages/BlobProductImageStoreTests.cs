using Azure.Storage.Blobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StockManager.ProductImages;
using Xunit;

namespace StockApi.Tests.ProductImages;

/// <summary>
/// The product-image store against a real blob service (T11), as <c>BlobDocumentStoreTests</c>
/// holds the document store: <c>DOCUMENTS_TEST_BLOB_CONNECTION</c>, skipped without it, a
/// container of its own per run.
/// </summary>
public sealed class BlobProductImageStoreTests : IAsyncLifetime
{
    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("DOCUMENTS_TEST_BLOB_CONNECTION");

    private const string SkipReason =
        "Set DOCUMENTS_TEST_BLOB_CONNECTION to an Azurite or storage account connection string to run these.";

    private BlobContainerClient? _container;
    private BlobProductImageStore _store = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        _container = new BlobServiceClient(ConnectionString).GetBlobContainerClient($"test-{Guid.NewGuid():N}");
        await _container.CreateAsync();
        _store = new BlobProductImageStore(_container, NullLogger<BlobProductImageStore>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DeleteIfExistsAsync();
        }
    }

    [SkippableFact]
    public async Task AStoredPhotographComesBackAsTheTypeItWasStoredAs()
    {
        Skip.If(_container is null, SkipReason);

        // Serving reads no row, so the type has to travel with the bytes.
        var name = await _store.SaveAsync(new MemoryStream([1, 2, 3]), ".webp", "image/webp");

        name.Should().MatchRegex(@"^[0-9a-f]{32}\.webp$");

        var image = await _store.OpenAsync(name);
        image.Should().NotBeNull();
        image!.ContentType.Should().Be("image/webp");

        using var copy = new MemoryStream();
        await image.Content.CopyToAsync(copy);
        copy.ToArray().Should().Equal(1, 2, 3);
    }

    [SkippableFact]
    public async Task ANameTheStoreDidNotGenerateNamesNothing()
    {
        Skip.If(_container is null, SkipReason);

        (await _store.OpenAsync("../documents/site-a/0123456789abcdef0123456789abcdef.pdf")).Should().BeNull();
        (await _store.DeleteAsync("0123456789abcdef0123456789abcdef.pdf")).Should().BeFalse();
    }

    [SkippableFact]
    public async Task ADeletedPhotographIsGone()
    {
        Skip.If(_container is null, SkipReason);

        var name = await _store.SaveAsync(new MemoryStream([1]), ".png", "image/png");

        (await _store.DeleteAsync(name)).Should().BeTrue();
        (await _store.OpenAsync(name)).Should().BeNull();
    }
}
