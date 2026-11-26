using System.Text.RegularExpressions;

namespace StockManager.ProductDocuments;

/// <summary>
/// What a product's document may be — a datasheet, a manual, a price list — and the only shape
/// its stored name takes.
/// </summary>
/// <remarks>
/// <c>ProductImageTypes</c>' rule: the bytes decide. A Word or Excel file cannot be told apart by
/// its bytes alone (every modern Office file is a ZIP, every older one an OLE compound file), so
/// the extension the uploader gave picks the type within the family the bytes prove, and nothing
/// outside it.
/// </remarks>
public static partial class ProductDocumentTypes
{
    /// <summary>Where a stored document is served, on both hosts.</summary>
    public const string PathPrefix = "/product-docs/";

    public const int SignatureLength = 8;

    /// <summary>The largest document accepted, 20 MB.</summary>
    public const long MaxBytes = 20 * 1024 * 1024;

    /// <summary>What to put in an upload control's <c>accept</c>.</summary>
    public const string Accept = ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx";

    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly Dictionary<string, string> OpenXml = new(StringComparer.OrdinalIgnoreCase)
    {
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    private static readonly Dictionary<string, string> Legacy = new(StringComparer.OrdinalIgnoreCase)
    {
        [".doc"] = "application/msword",
        [".xls"] = "application/vnd.ms-excel",
        [".ppt"] = "application/vnd.ms-powerpoint",
    };

    public static bool TryDetect(ReadOnlySpan<byte> leading, string? fileName, out string contentType, out string extension)
    {
        var declared = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        if (leading.StartsWith(Pdf))
        {
            (contentType, extension) = ("application/pdf", ".pdf");
            return true;
        }

        if (leading.StartsWith(Zip) && OpenXml.TryGetValue(declared, out var openXml))
        {
            (contentType, extension) = (openXml, declared);
            return true;
        }

        if (leading.StartsWith(Ole) && Legacy.TryGetValue(declared, out var legacy))
        {
            (contentType, extension) = (legacy, declared);
            return true;
        }

        (contentType, extension) = (string.Empty, string.Empty);
        return false;
    }

    public static bool IsStoredName(string? storedName) =>
        !string.IsNullOrEmpty(storedName) && Shape().IsMatch(storedName);

    /// <summary>
    /// The uploaded filename, cut to something safe to show and to name a download by. It is
    /// metadata only: the stored name is always generated.
    /// </summary>
    public static string DisplayName(string? fileName, string extension)
    {
        var name = new string(Path.GetFileName(fileName ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (name.Length == 0) name = "document" + extension;

        return name.Length <= 120 ? name : name[..(120 - extension.Length)] + extension;
    }

    [GeneratedRegex(@"^[0-9a-f]{32}\.(pdf|doc|docx|xls|xlsx|ppt|pptx)$")]
    private static partial Regex Shape();
}
