using Microsoft.AspNetCore.Identity;
using SMStore.Sites;
using StockManager.Identity;
using StockManager.Notifications;

namespace SMStore.Accounts;

/// <summary>What a posted new password did.</summary>
public enum PasswordResetResult
{
    /// <summary>The password changed. Every other session for that login is now dead.</summary>
    Succeeded,

    /// <summary>
    /// The link cannot be used: no token, a mangled one, an expired or already-used one, a
    /// user id that names nobody, or a login belonging to another store.
    /// </summary>
    /// <remarks>
    /// One value for all of those on purpose. Separating them would tell an unauthenticated
    /// caller which user ids exist and which store they belong to, and the person's next step
    /// is the same in every case: ask for a fresh link.
    /// </remarks>
    LinkNotUsable,

    /// <summary>
    /// The link was good and the password was not. Safe to explain, because Identity's
    /// password messages describe the store's rules rather than the account.
    /// </summary>
    PasswordRejected
}

/// <param name="Messages">
/// Only ever password-policy text, and only for <see cref="PasswordResetResult.PasswordRejected"/>.
/// </param>
public sealed record PasswordResetOutcome(PasswordResetResult Result, IReadOnlyList<string> Messages)
{
    public static PasswordResetOutcome LinkNotUsable { get; } =
        new(PasswordResetResult.LinkNotUsable, []);

    public bool Succeeded => Result == PasswordResetResult.Succeeded;
}

/// <summary>
/// The two halves of password reset: mailing a link, and spending it.
/// </summary>
/// <remarks>
/// A service rather than logic in the endpoint and the page, because the rules worth getting
/// right here are not rendering rules — they are which addresses get mail, what a failure is
/// allowed to say, and which store a token is good for. Those are testable without a browser
/// and are tested that way.
///
/// The tokens are ASP.NET Identity's. <c>ResetPasswordAsync</c> validates against the user's
/// security stamp and rotates it on success, which is what makes a link single-use, expiring,
/// and fatal to every session that login had open — see <c>CustomerSessionValidator</c>, which
/// is the half of that last part this codebase has to supply.
/// </remarks>
public sealed class PasswordResetService
{
    /// <summary>
    /// The one error code from <c>ResetPasswordAsync</c> that is about the link rather than
    /// the password. <c>IdentityErrorDescriber.InvalidToken</c> sets it from its own name.
    /// </summary>
    private const string InvalidTokenCode = "InvalidToken";

    private readonly UserManager<IdentityUser> _users;
    private readonly ISiteContext _siteContext;
    private readonly IEmailSender _sender;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        UserManager<IdentityUser> users,
        ISiteContext siteContext,
        IEmailSender sender,
        ILogger<PasswordResetService> logger)
    {
        _users = users;
        _siteContext = siteContext;
        _sender = sender;
        _logger = logger;
    }

    /// <summary>
    /// Mails a reset link to the address, if the address is one this store can mail.
    /// </summary>
    /// <remarks>
    /// Returns nothing, and that is the point: the caller has nothing to report and therefore
    /// nothing it could accidentally reveal. An unknown address, an address at another store,
    /// an address whose owner never confirmed it, and a real one all take this path and leave
    /// the same trace in the response.
    ///
    /// Anything else would make this the account-enumeration oracle that the registration form
    /// and the sign-in page both refuse to be, and a cheaper one than either: no password, no
    /// paperwork, just an address to test.
    /// </remarks>
    public async Task RequestAsync(string? email, CancellationToken cancellationToken = default)
    {
        var site = _siteContext.Site;

        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        // The login is qualified by store, so an address registered at another store simply
        // does not exist here and there is no cross-store comparison to get wrong.
        var user = await _users.FindByNameAsync(SiteQualifiedUserName.For(site.SiteKey, email.Trim()));

        /*
        An unconfirmed address gets no reset mail.

        Nobody has shown they own it, so a reset token sent there is a credential handed to
        whoever typed the address into the registration form. The route for that person is
        /confirm-email, which the forgot-password page offers to everybody rather than only to
        the people who need it — offering it selectively would be the disclosure this method
        is written to avoid.
        */
        if (user is null || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogInformation(
                "Password reset request at {SiteKey} produced no mail; answered neutrally.",
                site.SiteKey);

            return;
        }

        try
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);

            await _sender.SendAsync(
                PasswordResetEmails.ResetLink(
                    site, user.Email,
                    MailedTokenLink.For(
                        site, CustomerAuthentication.ResetPasswordPath, user.Id, token)),
                cancellationToken);

            _logger.LogInformation("Sent a password reset link at {SiteKey}.", site.SiteKey);
        }
        catch (Exception exception)
        {
            // Still no answer to the caller. Reporting a send failure would distinguish a real
            // address from one that produced no mail at all, which is the whole disclosure.
            _logger.LogError(exception,
                "Could not send a password reset link at {SiteKey}.", site.SiteKey);
        }
    }

    /// <summary>Spends a link on a new password.</summary>
    public async Task<PasswordResetOutcome> ResetAsync(
        string? identityUserId,
        string? encodedToken,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        var site = _siteContext.Site;
        var token = MailedTokenLink.TryDecode(encodedToken);

        if (string.IsNullOrWhiteSpace(identityUserId)
            || token is null
            || string.IsNullOrEmpty(newPassword))
        {
            return PasswordResetOutcome.LinkNotUsable;
        }

        var user = await _users.FindByIdAsync(identityUserId);

        if (user is null)
        {
            return PasswordResetOutcome.LinkNotUsable;
        }

        /*
        The store is checked as well as the token.

        A reset token is evidence about one login and says nothing about which of the stores
        run from here issued it. Without this, a token minted on store A's domain would reset
        that customer's password through store B's domain — and store B's operator would have
        served the reset for a customer they are not supposed to know exists.
        */
        if (!SiteQualifiedUserName.BelongsTo(user.UserName ?? string.Empty, site.SiteKey))
        {
            _logger.LogInformation(
                "Refused a password reset for a login belonging to another store at {SiteKey}.",
                site.SiteKey);

            return PasswordResetOutcome.LinkNotUsable;
        }

        var result = await _users.ResetPasswordAsync(user, token, newPassword);

        if (!result.Succeeded)
        {
            _logger.LogInformation(
                "Password reset refused at {SiteKey}: {Errors}.",
                site.SiteKey,
                string.Join(", ", result.Errors.Select(error => error.Code)));

            /*
            A bad token and a bad password are told apart here, and only here.

            The password errors describe the store's own rules and are worth showing — a reset
            form that says "no" without saying why is a form people give up on. A token error
            is not: it becomes the same "ask for a new link" as everything else, so nothing
            distinguishes an expired token from a user id that names nobody.
            */
            return result.Errors.Any(error => error.Code == InvalidTokenCode)
                ? PasswordResetOutcome.LinkNotUsable
                : new PasswordResetOutcome(
                    PasswordResetResult.PasswordRejected,
                    result.Errors.Select(error => error.Description).ToArray());
        }

        _logger.LogInformation("Password reset at {SiteKey}.", site.SiteKey);

        try
        {
            // After the change, never before, and it must not be able to undo it: the password
            // has already been written by the time this runs, so a mail failure is logged and
            // chased rather than reported as a reset that did not happen.
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                await _sender.SendAsync(
                    PasswordResetEmails.PasswordChanged(site, user.Email), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Changed a password at {SiteKey} but could not send the notification.",
                site.SiteKey);
        }

        return new PasswordResetOutcome(PasswordResetResult.Succeeded, []);
    }
}
