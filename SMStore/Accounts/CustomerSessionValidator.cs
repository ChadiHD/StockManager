using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SMDataManager.Library.DataAccess;
using SMStore.Sites;

namespace SMStore.Accounts;

/// <summary>
/// Decides, on every request that carries a customer cookie, whether it is still good for
/// this store — and fills <see cref="CustomerContext"/> when it is.
/// </summary>
/// <remarks>
/// This exists because a valid cookie proves identity and nothing else.
///
/// StockApi and SMStore share a Data Protection key ring on purpose, so a cookie written by
/// either is readable by both. They also share a hostname in development, where cookies
/// ignore the port — an admin signed in to the API on localhost:7042 sends that cookie to the
/// storefront on localhost:7260. A distinct cookie name keeps the two apart, but a name is a
/// convention, not a control.
///
/// The control is here: the cookie's user is resolved through Contact -> Account -> Site with
/// the request's own site, and a user belonging to another store resolves to nothing. That is
/// the same predicate the storefront applies to every other scoped read, applied to the
/// session itself.
///
/// It hits the database on every authenticated request, which is a deliberate trade. The
/// alternative is to carry the account and group in claims and re-check occasionally, which
/// saves a round trip and means a suspended account keeps trading until its cookie expires.
/// Immediate revocation is worth one indexed lookup; IX_Contact_IdentityUser exists for it.
/// If it ever shows up in a profile, cache it per request rather than per session.
/// </remarks>
public sealed class CustomerSessionValidator
{
    /// <summary>
    /// Only an approved account may trade. Pending and Rejected have never been let in;
    /// Suspended has been let in and taken back out, and must stop working now rather than
    /// when a cookie happens to expire.
    /// </summary>
    private const string ApprovedStatus = "Approved";

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<CustomerSessionValidator>();

        var siteContext = services.GetRequiredService<ISiteContext>();

        // Health probes and anything else outside site resolution have no store to check
        // against. Nothing there is authenticated, but rejecting is the safe reading.
        if (!siteContext.IsResolved)
        {
            await RejectAsync(context);
            return;
        }

        var identityUserId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(identityUserId))
        {
            await RejectAsync(context);
            return;
        }

        var contacts = services.GetRequiredService<IContactData>();
        var contact = contacts.GetByIdentityUser(identityUserId, siteContext.Site.Id);

        if (contact is null)
        {
            // Either the cookie belongs to another store's customer, or to a staff member of
            // the API, or the contact has been deleted. All three are "not a customer here",
            // and the log does not distinguish them because the request cannot either.
            logger.LogInformation(
                "Rejected a customer cookie that resolves to no contact at {SiteKey}.",
                siteContext.Site.SiteKey);

            await RejectAsync(context);
            return;
        }

        if (!string.Equals(contact.Status, "Active", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(contact.AccountStatus, ApprovedStatus, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Rejected a customer cookie for a contact that is {ContactStatus} on an account " +
                "that is {AccountStatus}.", contact.Status, contact.AccountStatus);

            await RejectAsync(context);
            return;
        }

        services.GetRequiredService<CustomerContext>().SignedInAs(contact);
    }

    /// <summary>
    /// Drops the principal and clears the cookie. Rejecting without also signing out leaves
    /// the browser re-presenting a cookie that can never work again, which reads to the
    /// customer as being silently logged out on every page they open.
    /// </summary>
    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CustomerAuthentication.Scheme);
    }
}
