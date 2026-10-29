using Microsoft.AspNetCore.Mvc;

namespace SMStore.Payments;

/// <summary>The provider's webhook, and "Pay now" on an unpaid order (T13).</summary>
public static class PaymentEndpoints
{
    /// <summary>Set in each store's Stripe dashboard as <c>https://{domain}/webhooks/stripe</c>.</summary>
    public const string WebhookPath = "/webhooks/stripe";

    /// <summary>Under <c>/checkout</c>, so the checkout's rate limit counts it too.</summary>
    public const string PayPath = "/checkout/pay";

    public static IEndpointRouteBuilder MapPayments(this IEndpointRouteBuilder endpoints)
    {
        // No antiforgery: the provider posts JSON, and the signature is its proof. Resolved to a
        // store by its host like every request, so only that store's secret is tried.
        endpoints.MapPost(WebhookPath, async (HttpContext context, [FromServices] PaymentService payments) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var payload = await reader.ReadToEndAsync(context.RequestAborted);

            return payments.Apply(payload, context.Request.Headers["Stripe-Signature"]) switch
            {
                WebhookOutcome.BadSignature => Results.BadRequest(),
                WebhookOutcome.NotConfigured => Results.NotFound(),
                // Applied or ignored: either way the provider must not send it again.
                _ => Results.Ok()
            };
        });

        endpoints.MapPost(PayPath, async (
            [FromForm] string? token,
            [FromForm] string? reference,
            [FromServices] PaymentService payments,
            HttpContext context) =>
        {
            var url = await payments.StartAsync(token, reference, context.RequestAborted);

            if (url is not null)
            {
                return Results.Redirect(url);
            }

            // Back to the order, saying so. The order's own page decides whether it exists.
            var back = Ordering.BasketToken.IsWellFormed(token)
                ? $"{Ordering.CheckoutService.GuestOrderPath}/{token}"
                : string.IsNullOrWhiteSpace(reference)
                    ? "/"
                    : $"{Ordering.QuoteDecisionEndpoints.OrdersPath}/{Uri.EscapeDataString(reference)}";

            return Results.Redirect(back + "?payment=failed");
        });

        return endpoints;
    }
}
