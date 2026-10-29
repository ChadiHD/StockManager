using SMDataManager.Library.Models;

namespace StockApi.Fiscal;

/// <summary>
/// Issues the fiscal document a sale owes (T14): Brazil's NF-e through a provider, or anything
/// like it elsewhere. Chosen per store by <c>Site.FiscalIssuer</c>.
/// </summary>
/// <remarks>
/// Called by <see cref="FiscalDocumentDispatcher"/> only, at least once per sale: a host killed
/// after issuing and before recording issues again on the next claim. An issuer whose provider
/// takes an idempotency key should pass the order's reference as one.
///
/// Throw <see cref="FiscalDocumentRefusedException"/> when no retry can help, and anything else
/// when one might. An authority that answers later fits the same shape: throw while it is
/// still deciding, and the retry asks again by the order's reference.
/// </remarks>
public interface IFiscalDocumentIssuer
{
    /// <summary>The key in <see cref="SiteSettingKeys.FiscalIssuers"/>.</summary>
    string Key { get; }

    Task<FiscalDocumentIssued> IssueAsync(FiscalDocumentRequest request, CancellationToken cancellationToken);
}

/// <summary>The sale, as the order records it: the store, the order, its lines and where it went.</summary>
public sealed record FiscalDocumentRequest(
    SiteModel Site, OrderModel Order, IReadOnlyList<OrderLineModel> Lines, OrderAddressModel? DeliveryAddress);

/// <summary>What the issuer gave back: the document's number, and where it can be read if anywhere.</summary>
public sealed record FiscalDocumentIssued(string Number, string? Url = null);

/// <summary>The issuer, or the authority behind it, refused the document; retrying will not change that.</summary>
public sealed class FiscalDocumentRefusedException(string message) : Exception(message);

public static class FiscalServiceCollectionExtensions
{
    /// <summary>
    /// Every issuer. A new one is a line here and a key in <see cref="SiteSettingKeys.FiscalIssuers"/>;
    /// <c>FiscalIssuerKeysTests</c> fails if either is missing.
    /// </summary>
    public static IServiceCollection AddFiscalIssuers(this IServiceCollection services)
    {
        services.AddSingleton<IFiscalDocumentIssuer, LoggingFiscalIssuer>();

        return services;
    }
}
