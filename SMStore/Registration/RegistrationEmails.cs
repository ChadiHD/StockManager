using SMDataManager.Library.Models;
using StockManager.Notifications;

namespace SMStore.Registration;

/// <summary>
/// The copy for the one message registration sends.
/// </summary>
/// <remarks>
/// Written here rather than in a template because T3 owns the seam and T6 owns the
/// templates. When T6 arrives this file is what it replaces — the wording moves into a
/// per-site template and the call site keeps calling <c>IEmailSender</c>.
///
/// Nothing in here reveals anything the recipient did not already type. A trading
/// application is acknowledged to the address on the application, so an acknowledgement that
/// quoted account state would be an acknowledgement sent to whoever typed the address.
/// </remarks>
public static class RegistrationEmails
{
    /// <summary>
    /// Confirms an application was received.
    /// </summary>
    /// <remarks>
    /// Says nothing about whether the address was already registered, because
    /// <c>RegistrationService</c> answers a duplicate exactly as it answers a new
    /// application and this message must not be the thing that gives the game away. The
    /// "already registered" variant is T6's to write, and it is what finally gives a
    /// genuine duplicate applicant something to act on.
    /// </remarks>
    public static EmailMessage Acknowledgement(
        SiteModel site, string email, string? name, string? reference, string? confirmationLink) =>
        new(site.SiteKey, email, name,
            $"We have your application — {site.Name}",
            $"""
             Thanks for applying for a trade account with {site.Name}.

             {(string.IsNullOrWhiteSpace(reference) ? "" : $"Your reference is {reference}.\n")}
             {ConfirmationParagraph(confirmationLink)}
             Someone will check the details and the documents you sent, and email you when the
             account is open. That usually takes a working day or two.

             If you do not hear from us, reply to this message and we will find it.

             — {site.Name}
             """);

    /// <summary>
    /// A replacement confirmation link, for someone who asked for one.
    /// </summary>
    /// <remarks>
    /// Separate from the acknowledgement because it is not an acknowledgement: this person
    /// applied some time ago and is stuck. It says nothing about the state of the application,
    /// which is the reviewer's to report, and nothing that would differ depending on whether
    /// the account exists — the endpoint that sends it answers identically either way.
    /// </remarks>
    public static EmailMessage ConfirmationReminder(SiteModel site, string email, string confirmationLink) =>
        new(site.SiteKey, email, null,
            $"Confirm your email address — {site.Name}",
            $"""
             Somebody asked us to resend this, so here it is. Open the link to confirm this is
             your address:

             {confirmationLink}

             If that was not you, nothing has happened and you can ignore this message.

             — {site.Name}
             """);

    /// <summary>
    /// The confirmation link, or nothing.
    /// </summary>
    /// <remarks>
    /// Absent when the application produced no account, which includes the neutral answer to
    /// an address already registered here. The wording has to work either way: a message that
    /// said "we have sent you a link" and then carried none would tell the reader that their
    /// address is the one that already has an account.
    /// </remarks>
    private static string ConfirmationParagraph(string? confirmationLink) =>
        string.IsNullOrWhiteSpace(confirmationLink)
            ? string.Empty
            : $"""
               First, confirm this is your address by opening this link:

               {confirmationLink}

               You will not be able to sign in until you have.

               """;
}
