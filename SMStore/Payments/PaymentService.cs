using Microsoft.AspNetCore.DataProtection;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Ordering;
using SMStore.Sites;
using StockManager.Payments;
using static SMStore.Localization.StoreText;

namespace SMStore.Payments;

/// <summary>
/// Paying for a cart order, and hearing that it was paid (T13).
/// </summary>
/// <remarks>
/// **The redirect back from the hosted page proves nothing.** Anybody can type the success URL, so
/// only a signed webhook marks an order paid, through <c>spOrder_ApplyPaymentEvent</c>, once per
/// event.
///
/// The store's keys are read per call, from <c>spSite_GetPaymentSecrets</c>, and decrypted here.
/// They are never on <see cref="SiteModel"/>, which is cached and passed around.
/// </remarks>
public sealed class PaymentService
{
    /// <summary>How long a session holds the order's stock before Stripe expires it. Its minimum is thirty minutes.</summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(1);

    private readonly IPaymentProvider _provider;
    private readonly ISiteData _sites;
    private readonly IOrderData _orders;
    private readonly IDataProtector _protector;
    private readonly ISiteContext _siteContext;
    private readonly ICustomerContext _customer;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<PaymentService> _logger;
    private SitePaymentSecrets? _secrets;

    public PaymentService(
        IPaymentProvider provider,
        ISiteData sites,
        IOrderData orders,
        IDataProtectionProvider protection,
        ISiteContext siteContext,
        ICustomerContext customer,
        IHttpContextAccessor http,
        ILogger<PaymentService> logger)
    {
        _provider = provider;
        _sites = sites;
        _orders = orders;
        _protector = PaymentSecrets.Protector(protection);
        _siteContext = siteContext;
        _customer = customer;
        _http = http;
        _logger = logger;
    }

    /// <summary>Whether this store takes payment: both its keys are set.</summary>
    public bool IsAvailable => Secrets().IsConfigured;

    /// <summary>
    /// A hosted payment page for an unpaid cart order, or null when there cannot be one.
    /// </summary>
    /// <remarks>
    /// The order is found the way its page finds it: by a guest's token, or by reference on the
    /// signed-in customer's account. A fresh session replaces any earlier one, which is expired, so
    /// a customer who started paying twice can finish once. A session the order would no longer take
    /// (it was paid or cancelled meanwhile) is expired at once and not used.
    /// </remarks>
    public async Task<string?> StartAsync(string? guestToken, string? reference, CancellationToken cancellationToken)
    {
        var secrets = Secrets();
        var (order, returnPath) = Find(guestToken, reference);

        if (!secrets.IsConfigured || order is not { AccountId: { } accountId }
            || order.Status != OrderStatus.AwaitingPayment)
        {
            return null;
        }

        var site = _siteContext.Site;
        var key = _protector.Unprotect(secrets.SecretRef);
        var back = $"{Origin()}{returnPath}";

        try
        {
            var session = await _provider.CreateSessionAsync(key, new PaymentRequest(
                order.Reference,
                order.Currency ?? site.CurrencyCode,
                order.AccountEmail,
                Lines(order, _orders.GetOrderLinesForAccount(order.Id, accountId, site.Id), site.PricesIncludeTax),
                back + "?paid=1",
                back,
                DateTimeOffset.UtcNow.Add(SessionLifetime)), cancellationToken);

            var change = _orders.SetPaymentSession(site.Id, order.Reference, session.Id);

            if (!change.Recorded)
            {
                await _provider.ExpireSessionAsync(key, session.Id, cancellationToken);
                return null;
            }

            if (change.PreviousSessionId is { } previous)
            {
                await _provider.ExpireSessionAsync(key, previous, cancellationToken);
            }

            return session.Url;
        }
        catch (Exception exception) when (exception is PaymentProviderException or HttpRequestException
                                          or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // The order stands, unpaid, and its page offers to try again.
            _logger.LogError(exception, "Could not start payment for order {Reference} at {SiteKey}.",
                order.Reference, site.SiteKey);

            return null;
        }
    }

    /// <summary>
    /// Applies a webhook delivery. Its signature must be this store's: the host named the store,
    /// and only that store's secret is tried.
    /// </summary>
    public WebhookOutcome Apply(string payload, string? signatureHeader)
    {
        var secrets = Secrets();

        if (!secrets.IsConfigured)
        {
            return WebhookOutcome.NotConfigured;
        }

        var site = _siteContext.Site;
        var received = _provider.ReadEvent(payload, signatureHeader, _protector.Unprotect(secrets.WebhookSecretRef), DateTimeOffset.UtcNow);

        if (received is null)
        {
            _logger.LogWarning("A payment webhook at {SiteKey} was not signed with its secret.", site.SiteKey);
            return WebhookOutcome.BadSignature;
        }

        if (received.Outcome is not { } outcome || string.IsNullOrEmpty(received.Reference))
        {
            return WebhookOutcome.Ignored;
        }

        var result = _orders.ApplyPaymentEvent(site.Id, received.Id, received.Type, received.Reference,
            received.SessionId, outcome);

        if (outcome == PaymentOutcome.Paid && result == "Unchanged")
        {
            // Money for an order that was not waiting for it: cancelled by an admin, or paid twice.
            // Nothing here can put that right, so a person has to.
            _logger.LogError("Payment event {EventId} at {SiteKey} paid order {Reference}, which was not awaiting payment. " +
                             "Refund or reinstate it by hand.", received.Id, site.SiteKey, received.Reference);
        }
        else
        {
            _logger.LogInformation("Payment event {EventId} ({Type}) at {SiteKey} for {Reference}: {Result}.",
                received.Id, received.Type, site.SiteKey, received.Reference, result);
        }

        return WebhookOutcome.Applied;
    }

    /// <summary>
    /// The order's lines as the hosted page shows them, adding up to exactly what the order charges.
    /// </summary>
    /// <remarks>
    /// On an inclusive store a line's price has its tax in, and so does the delivery, so they add up
    /// to the total as they are. On an exclusive store they are net, and the tax is a line of its own.
    /// A total that does not add up, which the procedure's arithmetic should make impossible, is sent
    /// as one line for the whole order, so the customer is never charged a different figure.
    /// </remarks>
    public static IReadOnlyList<PaymentLine> Lines(OrderModel order, IEnumerable<OrderLineModel> lines, bool inclusive)
    {
        var items = lines.Select(line => new PaymentLine(line.Name, line.Quantity, line.Price)).ToList();

        if (order.ShippingPrice > 0)
        {
            items.Add(new PaymentLine(order.ShippingName ?? L["Delivery"], 1, order.ShippingPrice));
        }

        if (!inclusive && order.VAT > 0)
        {
            items.Add(new PaymentLine(string.IsNullOrWhiteSpace(order.TaxTreatment) ? L["Tax"] : order.TaxTreatment, 1, order.VAT));
        }

        return items.Sum(item => item.Quantity * item.UnitAmount) == order.FinalPrice
            ? items
            : [new PaymentLine(L["Order {0}", order.Reference], 1, order.FinalPrice)];
    }

    private (OrderModel? Order, string ReturnPath) Find(string? guestToken, string? reference)
    {
        var siteId = _siteContext.Site.Id;

        if (BasketToken.IsWellFormed(guestToken))
        {
            return (_orders.GetOrderByGuestToken(guestToken!, siteId), $"{CheckoutService.GuestOrderPath}/{guestToken}");
        }

        if (_customer.AccountId is { } accountId && !string.IsNullOrWhiteSpace(reference))
        {
            return (_orders.GetOrderForAccount(reference, accountId, siteId),
                $"{QuoteDecisionEndpoints.OrdersPath}/{Uri.EscapeDataString(reference)}");
        }

        return (null, "/");
    }

    /// <summary>
    /// The request's own origin, for where the hosted page sends the customer back.
    /// </summary>
    /// <remarks>
    /// Not <c>Site.Domain</c>, unlike a mailed link. This URL goes to the provider from a request the
    /// host already resolved to this store, so its host is one this store owns. It also keeps a
    /// development port.
    /// </remarks>
    private string Origin()
    {
        var request = _http.HttpContext!.Request;
        return $"{request.Scheme}://{request.Host}";
    }

    private SitePaymentSecrets Secrets() =>
        _secrets ??= _sites.GetPaymentSecrets(_siteContext.Site.Id) ?? new SitePaymentSecrets();
}

public enum WebhookOutcome
{
    Applied,

    /// <summary>Signed and understood, but nothing this store acts on.</summary>
    Ignored,

    BadSignature,

    /// <summary>This store takes no payment, so nothing can be signed for it.</summary>
    NotConfigured
}
