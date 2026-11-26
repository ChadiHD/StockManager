using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace StockManager.ProductDocuments;

/// <summary>
/// A product's documents — datasheets, manuals — shared by every store as <c>dbo.Product</c> is.
/// </summary>
/// <remarks>
/// <c>IProductImageStore</c>'s shape. Public like a photograph, because a datasheet is part of
/// the shop window, but always served as a download: nothing an admin uploaded is rendered in
/// an origin that holds a session.
/// </remarks>
public interface IProductDocumentStore
{
    /// <summary>Stores the bytes and returns the generated name. The filename is kept only to name the download.</summary>
    Task<string> SaveAsync(Stream content, string extension, string contentType, string displayName, CancellationToken cancellationToken = default);

    /// <summary>The bytes, type and download name, or null for a name this store did not generate or does not hold.</summary>
    Task<ProductDocumentContent?> OpenAsync(string storedName, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string storedName, CancellationToken cancellationToken = default);
}

public sealed record ProductDocumentContent(Stream Content, string ContentType, string? ContentDisposition);

/// <summary>Product documents in their own private container, <c>product-documents</c>.</summary>
/// <remarks>
/// The download name rides on the blob as its Content-Disposition, so serving one reads no row,
/// as serving a photograph reads none.
/// </remarks>
public sealed class BlobProductDocumentStore : IProductDocumentStore
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobProductDocumentStore> _logger;

    public BlobProductDocumentStore(BlobContainerClient container, ILogger<BlobProductDocumentStore> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        Stream content, string extension, string contentType, string displayName, CancellationToken cancellationToken = default)
    {
        var storedName = $"{Guid.NewGuid():N}{extension}";

        // filename* is RFC 5987-encoded, so a name in any script, or with a quote in it, cannot
        // break out of the header.
        var disposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = displayName };

        await _container.GetBlobClient(storedName).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType, ContentDisposition = disposition.ToString() },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, cancellationToken);

        return storedName;
    }

    public async Task<ProductDocumentContent?> OpenAsync(string storedName, CancellationToken cancellationToken = default)
    {
        if (!ProductDocumentTypes.IsStoredName(storedName))
        {
            return null;
        }

        try
        {
            var download = await _container.GetBlobClient(storedName)
                .DownloadStreamingAsync(cancellationToken: cancellationToken);

            return new ProductDocumentContent(
                download.Value.Content, download.Value.Details.ContentType, download.Value.Details.ContentDisposition);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            _logger.LogWarning("Product document {StoredName} was asked for and is not in the store.", storedName);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string storedName, CancellationToken cancellationToken = default)
    {
        if (!ProductDocumentTypes.IsStoredName(storedName))
        {
            return false;
        }

        var deleted = await _container.GetBlobClient(storedName).DeleteIfExistsAsync(cancellationToken: cancellationToken);

        return deleted.Value;
    }
}

public static class ProductDocumentExtensions
{
    /// <summary>The app host's blob container, beside <c>product-images</c> in the same storage account.</summary>
    public const string ContainerName = "product-documents";

    /// <summary>
    /// Registers the product-document store. Both hosts call it after <c>AddDocumentStore</c>,
    /// and reach the container through the documents client's account for the reason
    /// <c>AddProductImageStore</c> gives.
    /// </summary>
    public static TBuilder AddProductDocumentStore<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSingleton<IProductDocumentStore>(services => new BlobProductDocumentStore(
            services.GetRequiredService<BlobContainerClient>().GetParentBlobServiceClient().GetBlobContainerClient(ContainerName),
            services.GetRequiredService<ILogger<BlobProductDocumentStore>>()));

        return builder;
    }

    /// <summary><c>GET /product-docs/{name}</c>: a product's document, to anybody, as a download.</summary>
    public static IEndpointRouteBuilder MapProductDocuments(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ProductDocumentTypes.PathPrefix + "{name}", async (
            string name, IProductDocumentStore store, HttpContext context, CancellationToken cancellationToken) =>
        {
            var document = await store.OpenAsync(name, cancellationToken);
            if (document is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.ContentDisposition = document.ContentDisposition ?? "attachment";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

            return Results.Stream(document.Content, document.ContentType);
        });

        return endpoints;
    }
}
