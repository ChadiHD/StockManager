using System.Text;

namespace StockManager.E2ETests.Infrastructure;

/// <summary>
/// A minimal but structurally real PDF, for the one file upload on the storefront
/// (registration's Chamber of Commerce document).
/// </summary>
/// <remarks>
/// StockManager.ServiceDefaults/Documents/DocumentContentTypes.cs identifies a file by its
/// first eight bytes -- "%PDF-" is a five-byte signature -- and never parses the rest, so
/// nothing here needs to satisfy a real PDF reader. It is built as one anyway (a catalog, a
/// one-page pages tree, a trailer) rather than the bare signature plus padding, so this stays
/// correct if that check ever grows past a magic-number sniff.
/// </remarks>
public static class TinyPdf
{
    public static byte[] Bytes { get; } = Encoding.ASCII.GetBytes(string.Join("\n",
    [
        "%PDF-1.4",
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj",
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj",
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj",
        "trailer<</Size 4/Root 1 0 R>>",
        "%%EOF",
        ""
    ]));
}
