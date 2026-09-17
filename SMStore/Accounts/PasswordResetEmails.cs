using SMDataManager.Library.Models;
using StockManager.Notifications;

namespace SMStore.Accounts;

/// <summary>
/// The copy for the two messages password reset sends.
/// </summary>
/// <remarks>
/// Written here rather than in a template for the same reason
/// <c>RegistrationEmails</c> is: T3 owns the seam and T6 owns the per-site templates, so this
/// file is what T6 replaces.
///
/// Neither message says anything about the account. A reset link is requested by anyone who
/// can type an address, so what goes to that address must be safe to send to a stranger who
/// guessed it — no company name, no account status, nothing but the link and how to ignore it.
/// </remarks>
public static class PasswordResetEmails
{
    public static EmailMessage ResetLink(SiteModel site, string email, string resetLink) =>
        new(site.SiteKey, email, null,
            $"Reset your password — {site.Name}",
            $"""
             Somebody asked to reset the password for this address at {site.Name}. Open the
             link to choose a new one:

             {resetLink}

             The link expires, and it stops working once it has been used.

             If that was not you, nothing has happened and you can ignore this message. Your
             current password still works.

             — {site.Name}
             """);

    /// <summary>
    /// Sent after a password actually changes.
    /// </summary>
    /// <remarks>
    /// The one message in this pair that a customer needs and did not ask for. Somebody whose
    /// mailbox has been taken over gets no reset mail — the attacker deletes it — but this one
    /// arrives after the fact and is the only signal the real owner gets that the account has
    /// gone. It is sent to the address on the login, never to anything the request supplied.
    /// </remarks>
    public static EmailMessage PasswordChanged(SiteModel site, string email) =>
        new(site.SiteKey, email, null,
            $"Your password was changed — {site.Name}",
            $"""
             The password for this address at {site.Name} has just been changed, and any
             browsers that were signed in have been signed out.

             If that was you, there is nothing to do.

             If it was not, contact us straight away — whoever changed it can sign in.

             — {site.Name}
             """);
}
