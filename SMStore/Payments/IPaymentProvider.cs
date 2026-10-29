namespace SMStore.Payments;

/// <summary>
/// A hosted payment page and the events that say how it went (T13).
/// </summary>
/// <remarks>
/// One implementation, <see cref="StripeCheckout"/>, built ahead of a second on purpose: a store in
/// Brazil will want a local provider, and this is the shape that one has to fill. It is also what
/// lets the checkout's tests run without the provider. Nothing outside this folder knows which
/// provider a store pays through.
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>
    /// Where the hosted page lives, for the storefront's CSP: Chrome applies <c>form-action</c> to
    /// the redirect that follows the checkout's post.
    /// </summary>
    string CheckoutOrigin { get; }

    /// <summary>A session for one order. Throws <see cref="PaymentProviderException"/> when refused.</summary>
    Task<PaymentSession> CreateSessionAsync(string secretKey, PaymentRequest request, CancellationToken cancellationToken);

    /// <summary>Ends a session nobody may pay through any more. Best effort: one already finished stays so.</summary>
    Task ExpireSessionAsync(string secretKey, string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// The event a webhook delivered, or null when its signature is not this store's or is stale.
    /// </summary>
    PaymentEvent? ReadEvent(string payload, string? signatureHeader, string webhookSecret, DateTimeOffset now);
}

/// <summary>What one order asks the customer to pay, line by line, adding up to its total.</summary>
public sealed record PaymentRequest(
    string Reference,
    string Currency,
    string? CustomerEmail,
    IReadOnlyList<PaymentLine> Lines,
    string SuccessUrl,
    string CancelUrl,
    DateTimeOffset ExpiresAt);

/// <summary>One line on the hosted page: an amount in the order's currency, not minor units.</summary>
public sealed record PaymentLine(string Name, int Quantity, decimal UnitAmount);

public sealed record PaymentSession(string Id, string Url);

/// <summary>
/// A provider event, read: which session, which order, and what it means for the order.
/// <see cref="Outcome"/> is null for an event this store does not act on.
/// </summary>
public sealed record PaymentEvent(string Id, string Type, string? SessionId, string? Reference, SMDataManager.Library.Models.PaymentOutcome? Outcome);

public sealed class PaymentProviderException(string message) : Exception(message);
