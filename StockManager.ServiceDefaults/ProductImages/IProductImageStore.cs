namespace StockManager.ProductImages;

/// <summary>
/// Product photographs (T11), shared by every store as <c>dbo.Product</c> is.
/// </summary>
/// <remarks>
/// <c>IDocumentStore</c>'s shape without the site key: a customer's paperwork belongs to one
/// store and must never be found from another, while a photograph of a shirt is the shirt's.
/// Unlike a document, it is meant to be public, and is served from <c>/img/{name}</c>.
/// </remarks>
public interface IProductImageStore
{
    /// <summary>Stores the bytes and returns the generated name. The uploaded filename is never used.</summary>
    Task<string> SaveAsync(Stream content, string extension, string contentType, CancellationToken cancellationToken = default);

    /// <summary>The bytes and their type, or null for a name this store did not generate or does not hold.</summary>
    Task<ProductImageContent?> OpenAsync(string storedName, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string storedName, CancellationToken cancellationToken = default);
}

public sealed record ProductImageContent(Stream Content, string ContentType);
