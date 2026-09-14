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
    public static EmailMessage Acknowledgement(SiteModel site, string email, string? name, string? reference) =>
        new(site.SiteKey, email, name,
            $"We have your application — {site.Name}",
            $"""
             Thanks for applying for a trade account with {site.Name}.

             {(string.IsNullOrWhiteSpace(reference) ? "" : $"Your reference is {reference}.\n")}
             Someone will check the details and the documents you sent, and email you when the
             account is open. That usually takes a working day or two.

             If you do not hear from us, reply to this message and we will find it.

             — {site.Name}
             """);
}
