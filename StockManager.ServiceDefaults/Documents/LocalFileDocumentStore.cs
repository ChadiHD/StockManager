using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StockManager.Documents;

public sealed class DocumentStoreOptions
{
    public const string SectionName = "Documents";

    /// <summary>
    /// The folder holding every store's container.
    /// </summary>
    /// <remarks>
    /// **Must be outside the web root.** A folder under wwwroot is served as static content,
    /// which would make every customer's paperwork downloadable by anyone who could guess a
    /// name — and the whole point of the generated name is that guessing is hard, which is
    /// not a property worth relying on alone. Unset, it defaults to a folder beside the
    /// content root rather than inside it.
    /// </remarks>
    public string? RootPath { get; set; }

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

/// <summary>
/// Files on the local filesystem, one folder per store. The development implementation, and
/// the one a single-server deployment can keep.
/// </summary>
public sealed partial class LocalFileDocumentStore : IDocumentStore
{
    private readonly string _root;
    private readonly ILogger<LocalFileDocumentStore> _logger;

    public LocalFileDocumentStore(
        IOptions<DocumentStoreOptions> options,
        string contentRootPath,
        ILogger<LocalFileDocumentStore> logger)
    {
        _logger = logger;

        // Beside the content root, not under it: anything under wwwroot is public, and the
        // difference between "app-data" and "wwwroot/app-data" is every customer's paperwork.
        _root = options.Value.RootPath is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(contentRootPath, "..", "app-data", "documents"));
    }

    public async Task<string> SaveAsync(
        string siteKey, Stream content, string extension, CancellationToken cancellationToken = default)
    {
        var container = Container(siteKey);
        Directory.CreateDirectory(container);

        var storedName = $"{Guid.NewGuid():N}{extension}";

        // CreateNew, not Create: a generated name should never collide, and if one somehow
        // does, overwriting one customer's document with another's is the worst outcome
        // available. Let it throw.
        await using var file = new FileStream(
            Path.Combine(container, storedName), FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        await content.CopyToAsync(file, cancellationToken);

        return storedName;
    }

    public Task<Stream?> OpenAsync(
        string siteKey, string storedName, CancellationToken cancellationToken = default)
    {
        if (!TryResolve(siteKey, storedName, out var path))
        {
            return Task.FromResult<Stream?>(null);
        }

        if (!File.Exists(path))
        {
            // A row with no file behind it. Worth a log line: it means either a half-finished
            // upload or a container restored without its volume.
            _logger.LogWarning(
                "Document {StoredName} is recorded for {SiteKey} but is not in the store.",
                storedName, siteKey);

            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true));
    }

    public Task<bool> DeleteAsync(
        string siteKey, string storedName, CancellationToken cancellationToken = default)
    {
        if (!TryResolve(siteKey, storedName, out var path) || !File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);

        return Task.FromResult(true);
    }

    private string Container(string siteKey)
    {
        if (!SafeSegment().IsMatch(siteKey))
        {
            // The site key comes from a resolved dbo.Site row, not from a request, so this
            // guards against a bad row rather than against a caller. It still throws: a key
            // that is not a safe path segment is a configuration error, and rewriting it
            // quietly would put two stores in one container.
            throw new ArgumentException(
                $"Site key '{siteKey}' is not usable as a storage container name.", nameof(siteKey));
        }

        return Path.Combine(_root, siteKey);
    }

    /// <summary>
    /// Turns a stored name into a path, refusing anything this store did not write.
    /// </summary>
    /// <remarks>
    /// The name arrives from a database column, which makes it trusted-ish and not trusted:
    /// the value that reaches here has passed through a query whose parameters came from a
    /// URL. So its shape is checked rather than assumed, and the result is checked to be
    /// inside the store's own container — belt and braces against a name that satisfies the
    /// pattern and still escapes.
    /// </remarks>
    private bool TryResolve(string siteKey, string storedName, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrEmpty(storedName) || !StoredNameShape().IsMatch(storedName))
        {
            _logger.LogWarning("Refused a document name that this store did not generate.");
            return false;
        }

        var container = Container(siteKey);
        var candidate = Path.GetFullPath(Path.Combine(container, storedName));

        if (!candidate.StartsWith(container + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            _logger.LogWarning("Refused a document path resolving outside {SiteKey}'s container.", siteKey);
            return false;
        }

        path = candidate;
        return true;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-]{0,63}$")]
    private static partial Regex SafeSegment();

    /// <summary>Exactly what <see cref="SaveAsync"/> generates: 32 hex digits and a known extension.</summary>
    [GeneratedRegex(@"^[0-9a-fA-F]{32}\.(pdf|png|jpg)$")]
    private static partial Regex StoredNameShape();
}
