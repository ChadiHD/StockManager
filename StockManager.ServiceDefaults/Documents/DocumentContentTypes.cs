namespace StockManager.Documents;

/// <summary>
/// The file types a customer may upload, recognised by their leading bytes.
/// </summary>
/// <remarks>
/// Neither the declared <c>Content-Type</c> nor the filename extension is evidence of
/// anything: both are chosen by whoever is uploading. The only thing that is not is the
/// content itself, so the allow-list is applied by reading the first few bytes and the
/// declared type is discarded entirely — what <see cref="TryDetect"/> returns is what gets
/// stored, served back, and written to <c>dbo.AccountDocument.ContentType</c>.
///
/// Three types, because three are what an application needs: a scan is a PDF or a photo. A
/// wider list is a wider parser surface on the reviewer's machine, and every addition should
/// have to justify itself.
///
/// This is not antivirus. A well-formed PDF can still carry something nasty, and scanning is
/// out of scope for T3 — see the plan's security section, where it is recorded as accepted
/// risk rather than overlooked.
/// </remarks>
public static class DocumentContentTypes
{
    /// <summary>
    /// How many leading bytes <see cref="TryDetect"/> needs. The longest signature is PNG's
    /// eight.
    /// </summary>
    public const int SignatureLength = 8;

    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];

    /// <summary>
    /// Identifies the content, or refuses it.
    /// </summary>
    /// <param name="leading">
    /// The first <see cref="SignatureLength"/> bytes of the file. A shorter span is refused
    /// rather than guessed at — a file too small to identify is too small to be a document.
    /// </param>
    /// <param name="contentType">The media type to store and to serve it back as.</param>
    /// <param name="extension">
    /// The extension for the stored name, including the dot. The uploaded filename never
    /// reaches the filesystem, so this is the only thing that decides it.
    /// </param>
    public static bool TryDetect(ReadOnlySpan<byte> leading, out string contentType, out string extension)
    {
        if (leading.StartsWith(Pdf))
        {
            (contentType, extension) = ("application/pdf", ".pdf");
            return true;
        }

        if (leading.StartsWith(Png))
        {
            (contentType, extension) = ("image/png", ".png");
            return true;
        }

        // The fourth byte varies by JPEG flavour (JFIF, Exif, raw SOI), so three is the whole
        // signature rather than a prefix of one.
        if (leading.StartsWith(Jpeg))
        {
            (contentType, extension) = ("image/jpeg", ".jpg");
            return true;
        }

        (contentType, extension) = (string.Empty, string.Empty);
        return false;
    }

    /// <summary>What to put in an upload control's <c>accept</c>, and in the help text.</summary>
    public const string Accept = ".pdf,.png,.jpg,.jpeg";
}
