using System.Text.RegularExpressions;

namespace StockManager.Documents;

/// <summary>
/// The two names a stored document is found by, and the only shapes either may take.
/// </summary>
/// <remarks>
/// A stored name arrives from a query whose parameters came out of a URL, so it is checked
/// against the shape this platform generates before it names anything in storage — an
/// uploaded filename never does. The site key comes from a resolved dbo.Site row; a key that is
/// not a safe segment is a configuration error, and rewriting it quietly would put two stores'
/// paperwork under one prefix.
///
/// Moved out of the local file store when documents moved to Blob storage (T7), so the checks
/// outlive whichever store holds the bytes.
/// </remarks>
public static partial class DocumentNames
{
    public static bool IsStoredName(string? storedName) =>
        !string.IsNullOrEmpty(storedName) && StoredNameShape().IsMatch(storedName);

    public static void RequireSiteKey(string siteKey)
    {
        if (string.IsNullOrEmpty(siteKey) || !SafeSegment().IsMatch(siteKey))
        {
            throw new ArgumentException(
                $"Site key '{siteKey}' is not usable as a storage prefix.", nameof(siteKey));
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-]{0,63}$")]
    private static partial Regex SafeSegment();

    [GeneratedRegex(@"^[0-9a-fA-F]{32}\.(pdf|png|jpg)$")]
    private static partial Regex StoredNameShape();
}
