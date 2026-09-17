using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SMStore.Registration;
using SMStore.Sites;
using StockManager.Identity;
using StockManager.Notifications;

namespace SMStore.Accounts;

/// <summary>
/// Sends a fresh confirmation link to an address that may or may not have an account.
/// </summary>
/// <remarks>
/// This exists because gating sign-in on a confirmed address creates a dead end without it. An
/// applicant who never received the mail, or deleted it, or let the token expire, would have no
/// route in at all and no way to ask for one — and staff could not fix it from the portal,
/// because the flag lives in ApiAuthDb and the admin screens read SMDatabase.
/// </remarks>
public static class EmailConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapEmailConfirmation(this IEndpointRouteBuilder endpoints)
    {
        // Antiforgery stays on, as on the other two form endpoints: without it any page on the
        // web could make a visitor's browser fire confirmation mail at an address of the
        // attacker's choosing, using this store as the sender.
        endpoints.MapPost(CustomerAuthentication.ResendConfirmationPath, ResendAsync);

        return endpoints;
    }

    private static async Task<IResult> ResendAsync(
        [FromForm] string? email,
        [FromServices] UserManager<IdentityUser> users,
        [FromServices] ISiteContext siteContext,
        [FromServices] IEmailSender sender,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(EmailConfirmationEndpoints));
        var site = siteContext.Site;

        /*
        Every path below answers identically, and the answer is the acknowledgement page.

        Saying "we have sent you a link" only when the address exists turns this form into the
        same oracle the registration form refuses to be — worse, in fact, because it needs no
        password and no paperwork, just an address to test. So an unknown address, an address
        at another store, and an address that is already confirmed all produce the same page
        and no mail.
        */
        var acknowledged = Results.Redirect($"{RegistrationPaths.RegisterPath}?resent=1");

        if (string.IsNullOrWhiteSpace(email))
        {
            return acknowledged;
        }

        var userName = SiteQualifiedUserName.For(site.SiteKey, email.Trim());
        var user = await users.FindByNameAsync(userName);

        if (user is null || user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogInformation(
                "Confirmation resend at {SiteKey} produced no mail; answered neutrally.", site.SiteKey);

            return acknowledged;
        }

        try
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);

            await sender.SendAsync(
                RegistrationEmails.ConfirmationReminder(
                    site, user.Email,
                    MailedTokenLink.For(
                        site, CustomerAuthentication.ConfirmEmailPath, user.Id, token)),
                cancellationToken);

            logger.LogInformation("Sent a fresh confirmation link at {SiteKey}.", site.SiteKey);
        }
        catch (Exception exception)
        {
            // Still the same answer. A failure here is ours to chase, and reporting it would
            // distinguish a real address from one that produced no mail at all.
            logger.LogError(exception,
                "Could not send a confirmation link at {SiteKey}.", site.SiteKey);
        }

        return acknowledged;
    }
}
