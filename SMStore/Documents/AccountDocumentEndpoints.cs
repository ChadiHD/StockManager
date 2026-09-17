using SMDataManager.Library.DataAccess;
using SMStore.Accounts;
using SMStore.Sites;
using StockManager.Documents;

namespace SMStore.Documents;

/// <summary>
/// Hands a customer back a file they uploaded.
/// </summary>
/// <remarks>
/// There is no static URL for a document and there is never going to be one. The bytes live
/// outside the web root, and the only way to them is this endpoint, which re-establishes
/// three things on every request: that the caller has a session on this store, that the
/// document belongs to this store, and that it belongs to *their* account. The generated
/// stored name is not a capability — it is not in any response, and it would not be enough
/// on its own if it were.
/// </remarks>
public static class AccountDocumentEndpoints
{
    public const string DownloadPath = "/account/documents/{id:int}";

    public static string DownloadUrl(int id) => $"/account/documents/{id}";

    public static IEndpointRouteBuilder MapAccountDocuments(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(DownloadPath, DownloadAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> DownloadAsync(
        int id,
        HttpContext http,
        ICustomerContext customer,
        IAccountDocumentData documents,
        IDocumentStore store,
        ISiteContext siteContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var site = siteContext.Site;

        // RequireAuthorization proves a cookie; CustomerSessionValidator is what proves the
        // cookie belongs to a customer of this store, and it is what fills this in.
        if (customer.AccountId is not { } accountId)
        {
            return Results.NotFound();
        }

        var document = documents.GetById(id, site.Id);

        /*
        404 for every refusal, including "exists but is not yours".

        403 would confirm the id names a real document, and the ids are sequential — so a
        signed-in customer could walk the range and learn how many applications this store
        has processed, which is not theirs to know. The site predicate is already inside
        spAccountDocument_GetById; the account check is here because only this layer knows
        who is asking.
        */
        if (document is null || document.AccountId != accountId)
        {
            loggerFactory.CreateLogger(typeof(AccountDocumentEndpoints)).LogInformation(
                "Refused document {DocumentId} to account {AccountId} at {SiteKey}.",
                id, accountId, site.SiteKey);

            return Results.NotFound();
        }

        var content = await store.OpenAsync(site.SiteKey, document.StoredName, cancellationToken);

        if (content is null)
        {
            return Results.NotFound();
        }

        // Belt and braces with the attachment disposition below. The content type was
        // sniffed on the way in rather than taken from the upload, so it is accurate — but a
        // browser that sniffs anyway is one XSS away from executing a "PDF", and the header
        // costs nothing.
        http.Response.Headers.XContentTypeOptions = "nosniff";

        // fileDownloadName sets Content-Disposition: attachment, which is the point: nothing
        // a customer uploaded should ever render inside this origin. It also RFC 6266-encodes
        // the name, which matters because the name is theirs and was never sanitised.
        return Results.File(content, document.ContentType, DownloadName(document.OriginalName));
    }

    /// <summary>
    /// Strips what a filename must not carry into a header, and nothing else.
    /// </summary>
    /// <remarks>
    /// Path separators and control characters go; everything else — spaces, accents, the
    /// customer's own naming — stays, because this is shown to the person who chose it.
    /// </remarks>
    private static string DownloadName(string? original)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return "document";
        }

        var cleaned = new string(original
            .Where(character => !char.IsControl(character) && character is not ('/' or '\\'))
            .ToArray())
            .Trim();

        return cleaned.Length == 0 ? "document" : cleaned;
    }
}
