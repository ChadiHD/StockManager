using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMStore.Sites;
using StockManager.Identity;

namespace SMStore.Accounts;

/// <summary>
/// Sign-in and sign-out, as form endpoints rather than Blazor components.
/// </summary>
/// <remarks>
/// A cookie is a response header, and by the time a Blazor SSR component renders, the
/// response has already begun. So the login page posts here, this writes the cookie, and the
/// browser is redirected back into the component world. The Blazor Web App templates do the
/// same thing for the same reason.
/// </remarks>
public static class CustomerAuthEndpoints
{
    public static IEndpointRouteBuilder MapCustomerAuth(this IEndpointRouteBuilder endpoints)
    {
        /*
        Antiforgery is left on, which means both forms must render <AntiforgeryToken />.

        A minimal API endpoint taking [FromForm] gets antiforgery validation by default, and
        turning it off here would be turning off the protection these two endpoints most
        need. Login CSRF is the underrated half: an attacker cannot read the victim's session
        but can log the victim into the attacker's account, and then watch what they do with
        it. Logout CSRF is only a nuisance, but it is a nuisance with no upside.
        */
        endpoints.MapPost(CustomerAuthentication.LoginPath, SignInAsync);
        endpoints.MapPost(CustomerAuthentication.LogoutPath, SignOutAsync);

        return endpoints;
    }

    private static async Task<IResult> SignInAsync(
        HttpContext http,
        [FromForm] string? email,
        [FromForm] string? password,
        [FromForm] string? returnUrl,
        [FromServices] UserManager<IdentityUser> users,
        [FromServices] IContactData contacts,
        [FromServices] ISiteContext siteContext,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(CustomerAuthEndpoints));
        var site = siteContext.Site;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return Failed(returnUrl);
        }

        // The login is qualified by store, so an address registered at another store simply
        // does not exist here — no cross-store sign-in is possible even before the checks
        // below, and there is nothing to compare that could be got wrong.
        var userName = SiteQualifiedUserName.For(site.SiteKey, email.Trim());
        var user = await users.FindByNameAsync(userName);

        /*
        Every failure below returns the same thing.

        Distinguishing "no such account", "wrong password", "not approved yet" and "suspended"
        would tell an unauthenticated caller which addresses hold accounts at this store, and
        whether a given company has been approved — commercially useful to a competitor and
        needing no credential to ask. An applicant who does not know why they cannot get in is
        expected to contact the store, which is also when a suspension gets explained properly.

        The password check runs even when the user does not exist, so the two paths take
        comparable time. CheckPasswordAsync on a null user would return immediately and turn
        the response time into the oracle the message refuses to be.
        */
        if (user is null)
        {
            // Hash a throwaway password so a missing account costs what a real one does.
            _ = users.PasswordHasher.HashPassword(new IdentityUser(), password);
            return Failed(returnUrl);
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            logger.LogInformation("Failed sign-in at {SiteKey}.", site.SiteKey);
            return Failed(returnUrl);
        }

        /*
        An unconfirmed address cannot sign in, and this is the check that makes
        EmailConfirmed mean something.

        Creating a login proves nothing about who owns the address: an applicant can type
        somebody else's, and approval is a staff member reading company details rather than
        verifying an inbox. Everything that matters later — the approval mail, and password
        reset when T6 adds it — is sent there, so the address has to be established before a
        session exists rather than after.

        It is checked here and not in CustomerSessionValidator on purpose. The validator runs
        on every authenticated request and already reads Contact -> Account -> Site; adding an
        Identity lookup to it would double that cost to re-answer a question that cannot change
        while a session is alive, because a session can only start here.
        */
        if (!user.EmailConfirmed)
        {
            logger.LogInformation(
                "Sign-in refused at {SiteKey}: the address has not been confirmed.", site.SiteKey);

            return Failed(returnUrl);
        }

        var contact = contacts.GetByIdentityUser(user.Id, site.Id);

        if (contact is null
            || !string.Equals(contact.Status, "Active", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(contact.AccountStatus, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Sign-in refused at {SiteKey}: contact {ContactStatus}, account {AccountStatus}.",
                site.SiteKey, contact?.Status ?? "missing", contact?.AccountStatus ?? "missing");

            return Failed(returnUrl);
        }

        /*
        The principal carries identity only.

        Account and group are deliberately absent: they decide prices and catalog visibility,
        and a claim is a value the server wrote once and then trusts. CustomerSessionValidator
        re-reads them from the database on every request, so a suspension or a group change
        takes effect on the next page rather than when a cookie expires.
        */
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, contact.FullName),
            new Claim(ClaimTypes.Email, contact.Email ?? string.Empty)
        ], CustomerAuthentication.Scheme));

        await http.SignInAsync(CustomerAuthentication.Scheme, principal);

        logger.LogInformation("Customer signed in at {SiteKey}.", site.SiteKey);

        return Results.Redirect(SafeReturnUrl(returnUrl));
    }

    private static async Task<IResult> SignOutAsync(HttpContext http, [FromForm] string? returnUrl)
    {
        await http.SignOutAsync(CustomerAuthentication.Scheme);

        return Results.Redirect(SafeReturnUrl(returnUrl));
    }

    private static IResult Failed(string? returnUrl) =>
        Results.Redirect(
            $"{CustomerAuthentication.LoginPath}?failed=1&returnUrl={Uri.EscapeDataString(SafeReturnUrl(returnUrl))}");

    /// <summary>
    /// Keeps a redirect on this site.
    /// </summary>
    /// <remarks>
    /// returnUrl arrives in a form post, so it is attacker-supplied. Redirecting to whatever
    /// it says turns the login page into an open redirect — a link that starts on the store's
    /// real domain, authenticates, and lands somewhere else, which is exactly the shape a
    /// phishing page wants. Only a local path is honoured; anything else falls back to the
    /// home page.
    /// </remarks>
    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/')
        // "//evil.example" and "/\evil.example" are both protocol-relative once a browser
        // sees them, and both start with a slash.
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
