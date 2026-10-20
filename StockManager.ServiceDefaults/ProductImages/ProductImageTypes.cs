namespace StockManager.ProductImages;

/// <summary>
/// The image types a product photograph may be (T11), recognised by their leading bytes.
/// </summary>
/// <remarks>
/// <c>DocumentContentTypes</c>'s rule, for <c>DocumentContentTypes</c>'s reason: the declared
/// type and the filename are the uploader's to choose, so only the bytes decide. Its own list
/// rather than that one, because that one admits a PDF, which is right for paperwork and wrong
/// for a shop window, and knows no WebP, which is what a photograph exported for the web often is.
/// </remarks>
public static class ProductImageTypes
{
    /// <summary>The bytes <see cref="TryDetect"/> needs: WebP is a RIFF header of twelve.</summary>
    public const int SignatureLength = 12;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();

    public static bool TryDetect(ReadOnlySpan<byte> leading, out string contentType, out string extension)
    {
        if (leading.StartsWith(Png))
        {
            (contentType, extension) = ("image/png", ".png");
            return true;
        }

        if (leading.StartsWith(Jpeg))
        {
            (contentType, extension) = ("image/jpeg", ".jpg");
            return true;
        }

        // RIFF, a four-byte length, then the form type.
        if (leading.Length >= SignatureLength && leading.StartsWith(Riff) && leading[8..12].SequenceEqual(Webp))
        {
            (contentType, extension) = ("image/webp", ".webp");
            return true;
        }

        (contentType, extension) = (string.Empty, string.Empty);
        return false;
    }

    /// <summary>What to put in an upload control's <c>accept</c>.</summary>
    public const string Accept = ".jpg,.jpeg,.png,.webp";

    /// <summary>The largest photograph accepted, 5 MB: a product shot, not a print master.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;
}
