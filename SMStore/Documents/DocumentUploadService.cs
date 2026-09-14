using Microsoft.Extensions.Options;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Registration;
using SMStore.Sites;
using StockManager.Documents;

namespace SMStore.Documents;

/// <summary>A file that passed validation, with the type it actually is.</summary>
/// <param name="Kind">
/// From <see cref="RegistrationDocumentRequirement.Kind"/>, so it is one of the values
/// <c>CK_AccountDocument_Kind</c> allows rather than anything the browser posted.
/// </param>
/// <param name="ContentType">Sniffed, never the declared header.</param>
public sealed record AcceptedDocument(
    string Kind, IFormFile File, string ContentType, string Extension);

/// <summary>What validation made of the files on a registration.</summary>
public sealed record DocumentUploadValidation(
    IReadOnlyList<RegistrationError> Errors, IReadOnlyList<AcceptedDocument> Accepted)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// The first customer-supplied bytes the platform accepts, and the rules they have to pass.
/// </summary>
/// <remarks>
/// Split into validate-then-attach on purpose, because the two happen either side of a write
/// this service does not own. An application is refused before an account exists, so a bad
/// file leaves nothing behind at all; the bytes only go to the store once there is a row for
/// them to belong to.
///
/// Within one document the order is the other way round and is fixed by
/// <c>spAccountDocument_Insert</c>: the file goes down first, then the row. A row with no
/// file is a broken download the reviewer sees; a file with no row is an orphan nobody
/// serves.
///
/// Antivirus is not here and is not anywhere. It is recorded as accepted risk in the T3
/// plan's security section — a reviewer opens these on their own machine, and the mitigation
/// is the narrow type allow-list plus the fact that nothing is executed server-side.
/// </remarks>
public sealed class DocumentUploadService
{
    private readonly IDocumentStore _store;
    private readonly IAccountDocumentData _documents;
    private readonly ISiteContext _siteContext;
    private readonly DocumentStoreOptions _options;
    private readonly ILogger<DocumentUploadService> _logger;

    public DocumentUploadService(
        IDocumentStore store,
        IAccountDocumentData documents,
        ISiteContext siteContext,
        IOptions<DocumentStoreOptions> options,
        ILogger<DocumentUploadService> logger)
    {
        _store = store;
        _documents = documents;
        _siteContext = siteContext;
        _options = options.Value;
        _logger = logger;
    }

    public long MaxBytes => _options.MaxBytes;

    /// <summary>
    /// Checks the paperwork against what the store asks for, and identifies each file by
    /// reading it rather than by believing it.
    /// </summary>
    /// <param name="posted">
    /// One entry per document requirement, in the field set's order. A requirement with no
    /// file posted is passed as null rather than omitted, so a missing required document is
    /// distinguishable from a form that never rendered the control.
    /// </param>
    public async Task<DocumentUploadValidation> ValidateAsync(
        IReadOnlyList<(RegistrationDocumentRequirement Requirement, IFormFile? File)> posted,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<RegistrationError>();
        var accepted = new List<AcceptedDocument>();

        foreach (var (requirement, file) in posted)
        {
            if (file is null || file.Length == 0)
            {
                if (requirement.IsRequired)
                {
                    errors.Add(new RegistrationError(null, $"{requirement.Label} is required."));
                }

                continue;
            }

            if (file.Length > _options.MaxBytes)
            {
                errors.Add(new RegistrationError(null,
                    $"{requirement.Label} is {file.Length / (1024 * 1024)} MB. " +
                    $"The limit is {_options.MaxBytes / (1024 * 1024)} MB."));

                continue;
            }

            await using var content = file.OpenReadStream();

            var leading = new byte[DocumentContentTypes.SignatureLength];
            var read = await content.ReadAtLeastAsync(
                leading, leading.Length, throwOnEndOfStream: false, cancellationToken);

            if (read < leading.Length
                || !DocumentContentTypes.TryDetect(leading, out var contentType, out var extension))
            {
                // Says nothing about what the file actually was. Naming the detected type
                // would turn this into a tool for probing what the sniffer recognises, and
                // the applicant only needs to know what to send instead.
                errors.Add(new RegistrationError(null,
                    $"{requirement.Label} must be a PDF, a JPEG or a PNG. That file is none of them, " +
                    "whatever it is named."));

                continue;
            }

            accepted.Add(new AcceptedDocument(requirement.Kind, file, contentType, extension));
        }

        return new DocumentUploadValidation(errors, accepted);
    }

    /// <summary>
    /// Stores validated files against an account that now exists, returning how many landed.
    /// </summary>
    /// <remarks>
    /// Failures here do not fail the registration. The account is already written and the
    /// applicant has already been told their application is in; unwinding it because a file
    /// would not save would be a worse outcome than a reviewer having to ask for the document
    /// again. So each failure is logged with the reference a reviewer can search for, and the
    /// bytes are removed if the row that would have pointed at them could not be written.
    /// </remarks>
    public async Task<int> AttachAsync(
        int accountId,
        int? contactId,
        IReadOnlyList<AcceptedDocument> accepted,
        CancellationToken cancellationToken = default)
    {
        var site = _siteContext.Site;
        var stored = 0;

        foreach (var document in accepted)
        {
            string? storedName = null;

            try
            {
                await using (var content = document.File.OpenReadStream())
                {
                    storedName = await _store.SaveAsync(
                        site.SiteKey, content, document.Extension, cancellationToken);
                }

                _documents.Insert(new AccountDocumentModel
                {
                    AccountId = accountId,
                    Kind = document.Kind,
                    StoredName = storedName,
                    // Kept for the reviewer to recognise, and untrusted for ever after. It is
                    // truncated to the column width here rather than by SQL Server, which
                    // would truncate silently.
                    OriginalName = Truncate(document.File.FileName, 260),
                    ContentType = document.ContentType,
                    SizeBytes = document.File.Length,
                    UploadedByContactId = contactId
                }, site.Id);

                stored++;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "Could not attach a {Kind} document to account {AccountId} at {SiteKey}. " +
                    "The applicant will have to be asked for it again.",
                    document.Kind, accountId, site.SiteKey);

                if (storedName is not null)
                {
                    // The row is what makes a file reachable, so a file whose row failed is
                    // unreachable by design. Remove it rather than leaving paperwork on disk
                    // that nothing accounts for.
                    await SafeDeleteAsync(site.SiteKey, storedName, cancellationToken);
                }
            }
        }

        return stored;
    }

    private async Task SafeDeleteAsync(string siteKey, string storedName, CancellationToken cancellationToken)
    {
        try
        {
            await _store.DeleteAsync(siteKey, storedName, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Orphaned document {StoredName} in {SiteKey} could not be removed.", storedName, siteKey);
        }
    }

    private static string Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) ? "document"
        : value.Length <= length ? value
        : value[..length];
}
