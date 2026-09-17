using Microsoft.AspNetCore.Mvc;
using SMStore.Accounts;

namespace SMStore.Ordering;

/// <summary>
/// Changing a basket, as form endpoints rather than component event handlers.
/// </summary>
/// <remarks>
/// The storefront renders with static SSR and no interactive render mode anywhere, so there is
/// no circuit for an <c>EventCallback</c> to arrive on: a button wired to one does nothing when
/// clicked. Every basket change is therefore a form post and a redirect, which is the same
/// shape sign-in and password reset already use, for the same reason.
///
/// That is a deliberate choice rather than a limitation worked around. Adding an interactive
/// island for a quantity box would put a circuit on the catalog and the basket — the two
/// heaviest anonymous pages — to save one round trip on a button nobody presses twice a second.
/// </remarks>
public static class BasketEndpoints
{
    public const string AddPath = "/basket/add";
    public const string QuantityPath = "/basket/quantity";
    public const string SubmitPath = "/basket/submit";

    /// <summary>Where a submitted basket lands. Route pair, like the basket page.</summary>
    public const string SubmittedPath = "/quote/submitted";

    public static IEndpointRouteBuilder MapBasket(this IEndpointRouteBuilder endpoints)
    {
        // Antiforgery is on, so every form must render <AntiforgeryToken />. A minimal API
        // endpoint taking [FromForm] validates it by default. It matters less here than on
        // sign-in — the worst a forged post achieves is an item in a stranger's basket — but
        // turning it off would be a deliberate downgrade for no gain.
        endpoints.MapPost(AddPath, AddAsync);
        endpoints.MapPost(QuantityPath, SetQuantityAsync);
        endpoints.MapPost(SubmitPath, SubmitAsync);

        return endpoints;
    }

    private static IResult AddAsync(
        [FromForm] string? sku,
        [FromForm] int? quantity,
        [FromForm] string? returnUrl,
        [FromServices] BasketService basket,
        [FromServices] OrderingModeProvider ordering)
    {
        // A quantity that did not parse, or arrived absent, is one. The procedures clamp the
        // range; this only decides what a missing box means, and "one of them" is what a
        // customer pressing Add on a page with no quantity input intends.
        var added = basket.Add(sku, quantity ?? 1);

        // Back where they were, so adding from the catalog does not lose their place in it.
        // Landing on the basket every time turns browsing into a page of back buttons.
        return Results.Redirect(Back(returnUrl, ordering, added ? null : "unavailable"));
    }

    private static IResult SetQuantityAsync(
        [FromForm] string? sku,
        [FromForm] int? quantity,
        [FromForm] string? returnUrl,
        [FromServices] BasketService basket,
        [FromServices] OrderingModeProvider ordering)
    {
        // Zero removes, which is what typing 0 into a quantity box means. A missing value is
        // treated as zero rather than as one: the only page that posts here renders the box,
        // so an absent value means it was cleared.
        basket.SetQuantity(sku, quantity ?? 0);

        return Results.Redirect(Back(returnUrl, ordering, null));
    }

    private static IResult SubmitAsync(
        [FromForm] string? note,
        [FromServices] BasketService basket,
        [FromServices] QuoteSubmissionService submissions,
        [FromServices] OrderingModeProvider ordering)
    {
        var current = basket.Current();

        if (current is null)
        {
            // Nothing to submit. A stale tab, or a second submit of a basket the first one
            // already consumed and deleted.
            return Results.Redirect(ordering.Current.BasketRoute + "?basket=empty");
        }

        var outcome = submissions.Submit(current.Id, note);

        if (!outcome.SignedIn)
        {
            // A quote belongs to an account, and a session is the only thing that names one.
            // Their basket survives the trip: spBasket_Claim merges it at sign-in.
            return Results.Redirect(
                $"{CustomerAuthentication.LoginPath}?returnUrl=" +
                Uri.EscapeDataString(ordering.Current.BasketRoute));
        }

        if (!outcome.HadAnythingToQuote)
        {
            return Results.Redirect(ordering.Current.BasketRoute + "?basket=all-unavailable");
        }

        if (!outcome.Succeeded)
        {
            // The procedure is all-or-nothing, so the basket is still there and retrying is
            // the right next move.
            return Results.Redirect(ordering.Current.BasketRoute + "?basket=failed");
        }

        var url = $"{SubmittedPath}?ref={Uri.EscapeDataString(outcome.Reference!)}";

        if (outcome.DroppedSkus.Count > 0)
        {
            url += "&dropped=" + Uri.EscapeDataString(string.Join(",", outcome.DroppedSkus));
        }

        return Results.Redirect(url);
    }

    /// <summary>
    /// Where to send the browser next, never anywhere a form field asked for.
    /// </summary>
    /// <remarks>
    /// returnUrl arrives in a post, so honouring it as given would make every basket button an
    /// open redirect — a link that starts on the store's real domain and lands somewhere else.
    /// Only a local path is taken; anything else falls back to the store's own basket. Same
    /// rule and the same reasoning as <c>CustomerAuthEndpoints.SafeReturnUrl</c>, which is
    /// where the protocol-relative cases are explained.
    /// </remarks>
    private static string Back(string? returnUrl, OrderingModeProvider ordering, string? notice)
    {
        var target = !string.IsNullOrWhiteSpace(returnUrl)
            && returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
                ? returnUrl
                : ordering.Current.BasketRoute;

        if (notice is null)
        {
            return target;
        }

        return target + (target.Contains('?') ? '&' : '?') + "basket=" + notice;
    }
}
