using System.Text.RegularExpressions;

namespace StockManager.ProductImages;

/// <summary>The only shape a stored image's name takes (T11), checked before it names anything.</summary>
/// <remarks>
/// The name comes back in a URL, so it is matched against what <see cref="IProductImageStore"/>
/// generates before it reaches storage, as <c>DocumentNames</c> does for documents.
/// </remarks>
public static partial class ProductImageNames
{
    /// <summary>
    /// Where a stored image is served, on both hosts. <c>spProductImage_Add</c> writes the same
    /// prefix into <c>Product.ProductImage</c>, so the two have to agree.
    /// </summary>
    public const string PathPrefix = "/img/";

    public static bool IsStoredName(string? storedName) =>
        !string.IsNullOrEmpty(storedName) && Shape().IsMatch(storedName);

    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png|webp)$")]
    private static partial Regex Shape();
}
