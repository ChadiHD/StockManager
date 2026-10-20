using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StockManager.ProductImages;

public static class ProductImageExtensions
{
    /// <summary>The app host's blob container, beside <c>documents</c> in the same storage account.</summary>
    public const string ContainerName = "product-images";

    /// <summary>
    /// Registers the product-image store (T11). Both hosts call it, after
    /// <c>AddDocumentStore</c>: the admin API writes photographs, and both serve them.
    /// </summary>
    /// <remarks>
    /// The container is taken from the documents client's own account rather than registered as
    /// a second Aspire client. A second, keyed registration took the unkeyed documents client's
    /// health check down with it ("No service for type BlobContainerClient"), so both hosts
    /// reported unhealthy and nothing waiting on them started. The app host declares both
    /// containers on one <c>storage</c> resource, which is what makes the shortcut true; a store
    /// whose photographs lived in another account would need its own client here.
    /// </remarks>
    public static TBuilder AddProductImageStore<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSingleton<IProductImageStore>(services => new BlobProductImageStore(
            services.GetRequiredService<BlobContainerClient>().GetParentBlobServiceClient().GetBlobContainerClient(ContainerName),
            services.GetRequiredService<ILogger<BlobProductImageStore>>()));

        return builder;
    }

    /// <summary>
    /// <c>GET /img/{name}</c>: a product photograph, to anybody.
    /// </summary>
    /// <remarks>
    /// Public because a shop window is, and from each host's own origin, so the storefront needs
    /// no third party in its CSP and the portal can show the same photograph without a token,
    /// which an <c>&lt;img&gt;</c> cannot carry. A name is never reused, so the response may be
    /// cached for a year. Every refusal is a 404.
    /// </remarks>
    public static IEndpointRouteBuilder MapProductImages(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ProductImageNames.PathPrefix + "{name}", async (
            string name, IProductImageStore store, HttpContext context, CancellationToken cancellationToken) =>
        {
            var image = await store.OpenAsync(name, cancellationToken);
            if (image is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

            return Results.Stream(image.Content, image.ContentType);
        });

        return endpoints;
    }
}
