using Microsoft.AspNetCore.Mvc;

namespace SMStore.Accounts;

/// <summary>
/// Takes an address and, if this store can mail it, sends a reset link.
/// </summary>
/// <remarks>
/// An endpoint rather than a form handler on the page because there is nothing to render: the
/// answer does not depend on the address, so it is a redirect to the acknowledgement rather
/// than a re-render carrying a result. The page that spends the link is a page, because that
/// one holds a token and has real errors to show.
/// </remarks>
public static class PasswordResetEndpoints
{
    public static IEndpointRouteBuilder MapPasswordReset(this IEndpointRouteBuilder endpoints)
    {
        // Antiforgery stays on, as on every other form endpoint here: without it any page on
        // the web could make a visitor's browser fire reset mail at an address of the
        // attacker's choosing, from this store's sending domain.
        endpoints.MapPost(CustomerAuthentication.RequestPasswordResetPath, RequestAsync);

        return endpoints;
    }

    private static async Task<IResult> RequestAsync(
        [FromForm] string? email,
        [FromServices] PasswordResetService resets,
        CancellationToken cancellationToken)
    {
        await resets.RequestAsync(email, cancellationToken);

        // One destination for every address, because the service has already decided that
        // every address gets the same treatment and a second answer here would undo it.
        return Results.Redirect($"{CustomerAuthentication.ForgotPasswordPath}?sent=1");
    }
}
