using SMDataManager.Library.Models;
using StockManager.Notifications;

namespace SMStore.Registration;

/// <summary>
/// The copy for the confirmation reminder, until it moves into the shared templates.
/// </summary>
/// <remarks>
/// The acknowledgement has already gone to <c>EmailTemplates.RegistrationReceived</c>, which
/// StockApi's dispatcher renders. This file goes with T6's item 7.
/// </remarks>
public static class RegistrationEmails
{
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
}
