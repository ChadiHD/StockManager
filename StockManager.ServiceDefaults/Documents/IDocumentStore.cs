namespace StockManager.Documents;

/// <summary>
/// Where customer-uploaded files live. The bytes only; the metadata is a row in
/// <c>dbo.AccountDocument</c>.
/// </summary>
/// <remarks>
/// A seam rather than a filesystem call, because development writes to a folder and
/// production writes to blob storage, and nothing above this should know which.
///
/// Every method takes the site key, and it is not a convenience. Files are laid out one
/// container per store, so a document can only be reached by a caller that has already
/// resolved the store it belongs to — the same predicate the database applies to the
/// metadata row, applied again to the bytes. A row read for the wrong store fails to resolve
/// a file even if the query that found it had no site filter at all.
/// </remarks>
public interface IDocumentStore
{
    /// <summary>
    /// Writes a file and returns the name it was stored under.
    /// </summary>
    /// <remarks>
    /// The name is generated here and the uploaded one is never used: a filename is
    /// attacker-supplied text, and putting it on a filesystem is how "..\..\web.config"
    /// becomes a path. The original is kept as metadata for display and nothing else.
    /// </remarks>
    /// <param name="extension">From <see cref="DocumentContentTypes"/>, not from the upload.</param>
    Task<string> SaveAsync(
        string siteKey, Stream content, string extension, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a stored file, or null if this store has no such file.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception for a missing file: the caller has already authorised
    /// the row, and a file that has gone missing behind a valid row is an operational problem
    /// to log, not a request to fail loudly at the customer.
    /// </remarks>
    Task<Stream?> OpenAsync(string siteKey, string storedName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a stored file. False when it was already gone.
    /// </summary>
    /// <remarks>
    /// Used to clean up after a metadata write that failed — the bytes go down first, so a
    /// failure there leaves a file nothing points at.
    /// </remarks>
    Task<bool> DeleteAsync(string siteKey, string storedName, CancellationToken cancellationToken = default);
}
