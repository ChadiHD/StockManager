namespace StockApi.Fiscal;

/// <summary>
/// Issues nothing, and logs the sale it was asked about (T14). For development, and for proving a
/// store's set-up before a provider is chosen. The settings screen says it issues nothing.
/// </summary>
/// <remarks>
/// The number it returns is plainly not a real one, so nobody mistakes the order screen for an
/// invoiced sale. Nothing personal is logged: the reference finds the rest.
/// </remarks>
public sealed class LoggingFiscalIssuer(ILogger<LoggingFiscalIssuer> logger) : IFiscalDocumentIssuer
{
    public string Key => "logging";

    public Task<FiscalDocumentIssued> IssueAsync(FiscalDocumentRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Fiscal document for {SiteKey} order {Reference}: {Lines} line(s), {Total} {Currency}, delivered to {Country}. Nothing was issued.",
            request.Site.SiteKey, request.Order.Reference, request.Lines.Count, request.Order.FinalPrice,
            request.Order.Currency, request.DeliveryAddress?.Country ?? "nowhere recorded");

        return Task.FromResult(new FiscalDocumentIssued($"LOG-{request.Order.Reference}"));
    }
}
