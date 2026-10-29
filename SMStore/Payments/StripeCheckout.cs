using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SMDataManager.Library.Models;

namespace SMStore.Payments;

/// <summary>
/// Stripe Checkout, the hosted page (T13), over <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// No Stripe SDK. This needs two calls and a signature check. That is less code to audit than a
/// package wrapping the whole API, and the request it sends is visible here.
///
/// **No <c>payment_method_types</c>.** The store's own Stripe dashboard decides what is offered:
/// cards everywhere, and Pix or boleto in Brazil. Those settle later, which is why the webhook
/// handles the asynchronous events too.
/// </remarks>
public sealed class StripeCheckout : IPaymentProvider
{
    /// <summary>Pinned, so a change in Stripe's default version cannot reshape what comes back.</summary>
    private const string ApiVersion = "2024-06-20";

    /// <summary>How old a signed event may be. Stripe's own libraries allow five minutes.</summary>
    private static readonly TimeSpan SignatureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Currencies Stripe takes in whole units. Every other is sent in hundredths.
    /// </summary>
    /// <remarks>
    /// ponytail: Stripe's documented list as of 2024; a new zero-decimal currency would be
    /// charged a hundred times over until it is added here.
    /// </remarks>
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV",
        "XAF", "XOF", "XPF"
    };

    private readonly HttpClient _http;
    private readonly StripeOptions _options;

    public StripeCheckout(HttpClient http, IOptions<StripeOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string CheckoutOrigin => _options.CheckoutOrigin;

    public async Task<PaymentSession> CreateSessionAsync(
        string secretKey, PaymentRequest request, CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "payment"),
            new("client_reference_id", request.Reference),
            new("metadata[reference]", request.Reference),
            new("success_url", request.SuccessUrl),
            new("cancel_url", request.CancelUrl),
            new("expires_at", request.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            form.Add(new("customer_email", request.CustomerEmail));
        }

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            var prefix = $"line_items[{i}]";

            form.Add(new($"{prefix}[quantity]", line.Quantity.ToString(CultureInfo.InvariantCulture)));
            form.Add(new($"{prefix}[price_data][currency]", request.Currency.ToLowerInvariant()));
            form.Add(new($"{prefix}[price_data][unit_amount]",
                MinorUnits(line.UnitAmount, request.Currency).ToString(CultureInfo.InvariantCulture)));
            form.Add(new($"{prefix}[price_data][product_data][name]", line.Name));
        }

        using var message = Post(secretKey, "v1/checkout/sessions", form);

        // One key per order and attempt. The resilience handler may retry a POST, and Stripe
        // answers a retry with the same key with the session it already made rather than a second.
        message.Headers.Add("Idempotency-Key", $"{request.Reference}-{Guid.NewGuid():N}");

        using var response = await _http.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PaymentProviderException($"Stripe refused the checkout session ({(int)response.StatusCode}): {ErrorMessage(body)}");
        }

        using var json = JsonDocument.Parse(body);

        return new PaymentSession(
            json.RootElement.GetProperty("id").GetString()!,
            json.RootElement.GetProperty("url").GetString()!);
    }

    public async Task ExpireSessionAsync(string secretKey, string sessionId, CancellationToken cancellationToken)
    {
        using var message = Post(secretKey, $"v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}/expire", []);
        using var response = await _http.SendAsync(message, cancellationToken);

        // A session already completed or expired answers 400, and is exactly what was wanted.
    }

    public PaymentEvent? ReadEvent(string payload, string? signatureHeader, string webhookSecret, DateTimeOffset now)
    {
        if (!IsSigned(payload, signatureHeader, webhookSecret, now))
        {
            return null;
        }

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        var type = root.GetProperty("type").GetString() ?? string.Empty;
        var session = root.GetProperty("data").GetProperty("object");

        string? Text(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        var reference = Text(session, "client_reference_id")
            ?? (session.TryGetProperty("metadata", out var metadata) ? Text(metadata, "reference") : null);

        PaymentOutcome? outcome = type switch
        {
            // A completed session whose payment is still on its way (boleto, Pix) is paid by the
            // asynchronous event that follows, not by this one.
            "checkout.session.completed" => Text(session, "payment_status") is "paid" or "no_payment_required"
                ? PaymentOutcome.Paid
                : PaymentOutcome.None,
            "checkout.session.async_payment_succeeded" => PaymentOutcome.Paid,
            "checkout.session.async_payment_failed" or "checkout.session.expired" => PaymentOutcome.Failed,
            _ => null
        };

        return new PaymentEvent(root.GetProperty("id").GetString()!, type, Text(session, "id"), reference, outcome);
    }

    /// <summary>
    /// Stripe's scheme: <c>t=timestamp,v1=signature</c>, the signature an HMAC-SHA256 of
    /// <c>{timestamp}.{payload}</c> under the endpoint's secret. Any v1 may match, because Stripe
    /// sends one per secret while a secret is being rolled. The timestamp bounds a replay.
    /// </summary>
    public static bool IsSigned(string payload, string? header, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        var parts = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToList();

        var timestamp = parts.FirstOrDefault(pair => pair[0] == "t")?[1];

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || (now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > SignatureTolerance)
        {
            return false;
        }

        var expected = Sign(payload, timestamp!, secret);

        return parts.Where(pair => pair[0] == "v1").Any(pair =>
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(pair[1]), Encoding.ASCII.GetBytes(expected)));
    }

    /// <summary>The v1 signature Stripe would send. Public for the journey, which signs its own events.</summary>
    public static string Sign(string payload, string timestamp, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));

    public static long MinorUnits(decimal amount, string currency) =>
        ZeroDecimal.Contains(currency)
            ? (long)Math.Round(amount, MidpointRounding.AwayFromZero)
            : (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private HttpRequestMessage Post(string secretKey, string path, IEnumerable<KeyValuePair<string, string>> form)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.ApiBase), path))
        {
            Content = new FormUrlEncodedContent(form)
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        message.Headers.Add("Stripe-Version", ApiVersion);

        return message;
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("error").GetProperty("message").GetString() ?? body;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return body;
        }
    }
}

/// <summary>
/// Where Stripe is (<c>Payments:Stripe</c>). Stripe's own by default. The journey points both at a
/// stub, which is the only reason they are settings.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Payments:Stripe";

    public string ApiBase { get; set; } = "https://api.stripe.com/";

    public string CheckoutOrigin { get; set; } = "https://checkout.stripe.com";
}
