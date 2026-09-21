using Microsoft.AspNetCore.Mvc;
using SMStore.Accounts;

namespace SMStore.Ordering;

/// <summary>
/// Accepting and rejecting a quote, as form endpoints.
/// </summary>
/// <remarks>
/// Form posts for the same reason the basket's are: static SSR has no circuit for an event to
/// arrive on, and accepting a quote is exactly the kind of action that must not be a GET —
/// anything that makes a browser fetch a URL would place an order.
/// </remarks>
public static class QuoteDecisionEndpoints
{
    public const string AcceptPath = "/account/quotes/accept";
    public const string RejectPath = "/account/quotes/reject";

    /// <summary>Where the customer's own quotes live.</summary>
    public const string QuotesPath = "/account/quotes";

    /// <summary>Where their orders live.</summary>
    public const string OrdersPath = "/account/orders";

    public static IEndpointRouteBuilder MapQuoteDecisions(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(AcceptPath, AcceptAsync);
        endpoints.MapPost(RejectPath, RejectAsync);

        return endpoints;
    }

    private static IResult AcceptAsync(
        [FromForm] string? reference,
        [FromForm] string? poNumber,
        [FromServices] QuoteDecisionService decisions,
        [FromServices] ICustomerContext customer)
    {
        if (!customer.IsSignedIn)
        {
            return SignIn(reference);
        }

        var decision = decisions.Accept(reference, poNumber);

        // An accepted quote becomes an order, so that is where the customer goes. Landing them
        // back on a quote that now reads "Accepted" would leave them looking for what happened.
        return decision.Succeeded
            ? Results.Redirect($"{OrdersPath}/{Uri.EscapeDataString(decision.OrderReference!)}")
            : Back(reference, decision);
    }

    private static IResult RejectAsync(
        [FromForm] string? reference,
        [FromForm] string? reason,
        [FromServices] QuoteDecisionService decisions,
        [FromServices] ICustomerContext customer)
    {
        if (!customer.IsSignedIn)
        {
            return SignIn(reference);
        }

        var decision = decisions.Reject(reference, reason);

        return Back(reference, decision);
    }

    /// <summary>
    /// A session ended mid-decision. Sends them to sign in and back to the quote afterwards.
    /// </summary>
    /// <remarks>
    /// Reachable: <c>CustomerSessionValidator</c> drops a cookie whose security stamp has
    /// rotated, so resetting a password in another tab ends this one between rendering the
    /// form and posting it.
    /// </remarks>
    private static IResult SignIn(string? reference) =>
        Results.Redirect(
            $"{CustomerAuthentication.LoginPath}?returnUrl=" +
            Uri.EscapeDataString(QuoteUrl(reference)));

    /// <summary>
    /// Back to the quote, carrying what happened as a known word.
    /// </summary>
    /// <remarks>
    /// The outcome's name rather than a message, because the page renders from an allow-list:
    /// a query parameter echoed back would put attacker-chosen text on a page inside a session.
    /// </remarks>
    private static IResult Back(string? reference, QuoteDecision decision) =>
        Results.Redirect($"{QuoteUrl(reference)}?decision={decision.Name}");

    /// <summary>
    /// The quote's own page, or the list when the reference is unusable.
    /// </summary>
    /// <remarks>
    /// The reference arrives in a form post, so it is escaped into the path rather than
    /// concatenated. It is not otherwise validated here: the page it lands on resolves it
    /// against the session's account and shows "not found" for anything else, which is the
    /// same answer it gives for another customer's reference.
    /// </remarks>
    private static string QuoteUrl(string? reference) =>
        string.IsNullOrWhiteSpace(reference)
            ? QuotesPath
            : $"{QuotesPath}/{Uri.EscapeDataString(reference.Trim())}";
}
