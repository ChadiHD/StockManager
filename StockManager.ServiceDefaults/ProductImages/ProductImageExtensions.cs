using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StockManager.ProductImages;

public static class ProductImageExtensions
{
    /// <summary>The app host's blob container resource, and so the connection name.</summary>
    public const string ContainerName = "product-images";

    /// <summary>
    /// Registers the product-image store (T11). Both hosts call it: the admin API writes
    /// photographs, and both serve them.
    /// </summary>
    /// <remarks>
    /// Keyed, because both hosts already hold one unkeyed <c>BlobContainerClient</c>, the
    /// documents container, and a second unkeyed registration would replace it.
    /// </remarks>
    public static TBuilder AddProductImageStore<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddKeyedAzureBlobContainerClient(ContainerName);
        builder.Services.AddSingleton<IProductImageStore, BlobProductImageStore>();

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
