namespace StockManager.Documents;

public sealed class DocumentStoreOptions
{
    public const string SectionName = "Documents";

    /// <summary>
    /// The largest file accepted, in bytes.
    /// </summary>
    /// <remarks>
    /// Ten megabytes: large enough for a scanned certificate, small enough that the request
    /// limit mirroring it is not a denial-of-service surface. The design artboard says
    /// 128 MB; that was drawn, not reasoned. Whatever this is set to, the request body limit
    /// on the upload endpoint has to agree — a cap enforced only in code is a cap enforced
    /// after the bytes have already been accepted.
    /// </remarks>
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
}
