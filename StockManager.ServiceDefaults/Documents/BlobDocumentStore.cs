using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace StockManager.Documents;

/// <summary>
/// Customer documents in one private blob container, under a prefix per store.
/// </summary>
/// <remarks>
/// Replaced the local file store in T7. Each container app has its own filesystem and a new
/// revision starts with an empty one, so files written there were invisible to the other host
/// and gone on the next deploy — a reviewer opening an application found every document 404.
/// Locally the app host runs Azurite, so the store the tests exercise is the one deployed.
///
/// The container is private and must stay so: there is no URL for a document, only the two
/// endpoints that check who is asking (see CLAUDE.md, Customer documents).
/// </remarks>
public sealed class BlobDocumentStore : IDocumentStore
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobDocumentStore> _logger;

    public BlobDocumentStore(BlobContainerClient container, ILogger<BlobDocumentStore> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        string siteKey, Stream content, string extension, CancellationToken cancellationToken = default)
    {
        DocumentNames.RequireSiteKey(siteKey);

        var storedName = $"{Guid.NewGuid():N}{extension}";

        // overwrite: false. A generated name should never collide, and if one somehow does,
        // replacing one customer's document with another's is the worst outcome available.
        await _container.GetBlobClient(BlobName(siteKey, storedName))
            .UploadAsync(content, overwrite: false, cancellationToken);

        return storedName;
    }

    public async Task<Stream?> OpenAsync(
        string siteKey, string storedName, CancellationToken cancellationToken = default)
    {
        if (!Accept(siteKey, storedName))
        {
            return null;
        }

        try
        {
            var download = await _container.GetBlobClient(BlobName(siteKey, storedName))
                .DownloadStreamingAsync(cancellationToken: cancellationToken);

            return download.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // A row with no blob behind it: a half-finished upload, or storage restored without
            // the database (or the other way round).
            _logger.LogWarning(
                "Document {StoredName} is recorded for {SiteKey} but is not in the store.",
                storedName, siteKey);

            return null;
        }
    }

    public async Task<bool> DeleteAsync(
        string siteKey, string storedName, CancellationToken cancellationToken = default)
    {
        if (!Accept(siteKey, storedName))
        {
            return false;
        }

        var deleted = await _container.GetBlobClient(BlobName(siteKey, storedName))
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);

        return deleted.Value;
    }

    private bool Accept(string siteKey, string storedName)
    {
        DocumentNames.RequireSiteKey(siteKey);

        if (DocumentNames.IsStoredName(storedName))
        {
            return true;
        }

        _logger.LogWarning("Refused a document name that this store did not generate.");
        return false;
    }

    private static string BlobName(string siteKey, string storedName) => $"{siteKey}/{storedName}";
}
