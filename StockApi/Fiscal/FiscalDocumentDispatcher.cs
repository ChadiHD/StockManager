using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Email;
using SMDataManager.Library.Models;

namespace StockApi.Fiscal;

/// <summary>
/// Issues one batch of owed fiscal documents (T14): claim, read the order, issue, record.
/// </summary>
/// <remarks>
/// <c>EmailDispatcher</c>'s shape: every failure is caught per document, retried on
/// <see cref="OutboxRetryPolicy"/>'s schedule, then dead-lettered with a mail to the store's
/// operator. A dead letter is not the end here, because the sale still owes the document; the
/// order screen sends it again.
/// </remarks>
public class FiscalDocumentDispatcher
{
    public const int BatchSize = 10;

    /// <summary>Longer than a mail's: a provider waiting on a tax authority can take minutes.</summary>
    public const int LeaseMinutes = 10;

    private readonly IFiscalDocumentData _documents;
    private readonly IOrderData _orders;
    private readonly ISiteData _sites;
    private readonly Dictionary<string, IFiscalDocumentIssuer> _issuers;
    private readonly IEmailOutbox _mail;
    private readonly ILogger<FiscalDocumentDispatcher> _logger;

    public FiscalDocumentDispatcher(
        IFiscalDocumentData documents,
        IOrderData orders,
        ISiteData sites,
        IEnumerable<IFiscalDocumentIssuer> issuers,
        IEmailOutbox mail,
        ILogger<FiscalDocumentDispatcher> logger)
    {
        _documents = documents;
        _orders = orders;
        _sites = sites;
        _issuers = issuers.ToDictionary(issuer => issuer.Key, StringComparer.OrdinalIgnoreCase);
        _mail = mail;
        _logger = logger;
    }

    /// <summary>Issues what is due. Returns how many documents were claimed.</summary>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        var claimed = _documents.Claim(Guid.NewGuid(), BatchSize, LeaseMinutes);

        if (claimed.Count == 0)
        {
            return 0;
        }

        var sites = _sites.GetSites().ToDictionary(site => site.Id);

        foreach (var row in claimed)
        {
            // The rest stay claimed until their lease runs out, then go.
            if (cancellationToken.IsCancellationRequested) break;

            await IssueAsync(row, sites, cancellationToken);
        }

        return claimed.Count;
    }

    private async Task IssueAsync(FiscalDocumentModel row, Dictionary<int, SiteModel> sites, CancellationToken cancellationToken)
    {
        sites.TryGetValue(row.SiteId, out var site);

        // Claimed more often than it is allowed attempts: each earlier claim ended with the host
        // dying mid-issue, because an issue that failed normally would have recorded it.
        if (row.Attempts > OutboxRetryPolicy.MaxAttempts)
        {
            GiveUp(row, site, "Abandoned mid-issue on every attempt; the host stopped while issuing it each time.");
            return;
        }

        try
        {
            if (site is null)
            {
                throw new InvalidOperationException($"Site {row.SiteId} could not be read.");
            }

            var issuer = _issuers.GetValueOrDefault(row.Issuer)
                ?? throw new InvalidOperationException(
                    $"No fiscal issuer is registered as '{row.Issuer}'. A host older than the row may be " +
                    "dispatching; it is retried in case a newer one arrives.");

            var order = _orders.GetOrderByReference(row.Reference, row.SiteId)
                ?? throw new InvalidOperationException($"Order {row.Reference} could not be read.");

            var issued = await issuer.IssueAsync(
                new FiscalDocumentRequest(
                    site, order, _orders.GetOrderLines(row.PurchaseId, row.SiteId),
                    _orders.GetDeliveryAddress(row.PurchaseId, row.SiteId)),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(issued.Number))
            {
                throw new InvalidOperationException($"The {issuer.Key} issuer returned no document number.");
            }

            if (!_documents.RecordIssued(row.Id, row.ClaimToken, issued.Number, issued.Url))
            {
                _logger.LogWarning(
                    "The fiscal document for {Reference} was issued as {Number}, but its claim had already been " +
                    "taken over, so it may be issued twice.", row.Reference, issued.Number);
            }
        }
        catch (FiscalDocumentRefusedException exception) when (!cancellationToken.IsCancellationRequested)
        {
            GiveUp(row, site, exception.Message);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var retryAfter = OutboxRetryPolicy.After(row.Attempts);

            if (retryAfter is null)
            {
                GiveUp(row, site, exception.Message);
                return;
            }

            _documents.RecordFailure(row.Id, row.ClaimToken, exception.Message, retryAfter);

            _logger.LogWarning(
                "The fiscal document for {Reference} failed on attempt {Attempts}; retrying in {RetryAfter}: {Error}",
                row.Reference, row.Attempts, retryAfter, exception.Message);
        }
    }

    /// <summary>
    /// Dead-letters the document and tells the store's operator. No platform-wide fallback when a
    /// store has none, for <c>FeedAlertService</c>'s reason: the message names this store's sale.
    /// </summary>
    private void GiveUp(FiscalDocumentModel row, SiteModel? site, string error)
    {
        _documents.RecordFailure(row.Id, row.ClaimToken, error, retryAfter: null);

        _logger.LogError(
            "The fiscal document for {Reference} on site {SiteId} was dead-lettered after {Attempts} attempt(s): {Error}",
            row.Reference, row.SiteId, row.Attempts, error);

        if (string.IsNullOrWhiteSpace(site?.OperatorEmail))
        {
            _logger.LogWarning(
                "Site {SiteId} has no OperatorEmail, so nobody was told the fiscal document for {Reference} failed.",
                row.SiteId, row.Reference);
            return;
        }

        try
        {
            _mail.Enqueue(site.Id, site.OperatorEmail, null, EmailTemplates.FiscalFailed,
                new FiscalFailedPayload(row.Reference, row.Issuer, row.Attempts, error));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not queue the fiscal-failure alert for {Reference}.", row.Reference);
        }
    }
}
