using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace StockManager.ProductImages;

/// <summary>
/// Product photographs in their own blob container, <c>product-images</c> (T11).
/// </summary>
/// <remarks>
/// A container of its own rather than a prefix in <c>documents</c>, because that one holds
/// customers' paperwork and nothing about a product should sit beside it. Still private: the
/// hosts serve the bytes from their own origin, so the storefront's CSP stays <c>img-src
/// 'self'</c> and nothing depends on public access being enabled on an account.
///
/// The content type rides on the blob, so serving a photograph reads no row.
/// </remarks>
public sealed class BlobProductImageStore : IProductImageStore
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobProductImageStore> _logger;

    public BlobProductImageStore(
        BlobContainerClient container,
        ILogger<BlobProductImageStore> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        Stream content, string extension, string contentType, CancellationToken cancellationToken = default)
    {
        var storedName = $"{Guid.NewGuid():N}{extension}";

        // overwrite: false, as for documents: a collision should never happen, and replacing one
        // product's photograph with another's is the wrong way for it to fail.
        await _container.GetBlobClient(storedName).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, cancellationToken);

        return storedName;
    }

    public async Task<ProductImageContent?> OpenAsync(string storedName, CancellationToken cancellationToken = default)
    {
        if (!ProductImageNames.IsStoredName(storedName))
        {
            return null;
        }

        try
        {
            var download = await _container.GetBlobClient(storedName)
                .DownloadStreamingAsync(cancellationToken: cancellationToken);

            return new ProductImageContent(download.Value.Content, download.Value.Details.ContentType);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            _logger.LogWarning("Product image {StoredName} was asked for and is not in the store.", storedName);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string storedName, CancellationToken cancellationToken = default)
    {
        if (!ProductImageNames.IsStoredName(storedName))
        {
            return false;
        }

        var deleted = await _container.GetBlobClient(storedName).DeleteIfExistsAsync(cancellationToken: cancellationToken);

        return deleted.Value;
    }
}
