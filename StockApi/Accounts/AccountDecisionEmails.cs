using SMDataManager.Library.Models;
using StockManager.Notifications;

namespace StockApi.Accounts
{
    /// <summary>
    /// The copy for the two messages an approval decision sends.
    /// </summary>
    /// <remarks>
    /// Written here rather than in a template because T3 owns the seam and T6 owns the
    /// templates. When T6 arrives this file is what it replaces — the wording moves into a
    /// per-site template and the call site keeps calling <c>IEmailSender</c>.
    /// </remarks>
    public static class AccountDecisionEmails
    {
        public static EmailMessage Approved(SiteModel site, AccountModel account) =>
            new(site.SiteKey, account.Email, account.ContactName,
                $"Your trade account is open — {site.Name}",
                $"Your application for {account.Company} has been approved.\n\n" +
                $"You can sign in at https://{site.Domain}/login with the email address you " +
                "applied with. Prices you see once signed in are your account's own.\n\n" +
                $"— {site.Name}");

        /// <summary>
        /// Tells an applicant they were turned down, and why.
        /// </summary>
        /// <remarks>
        /// The reason is quoted verbatim, which is why <c>spAccount_Reject</c> insists on
        /// one: this message is the whole of what the applicant learns, and "your application
        /// was unsuccessful" with no reason is what generates the phone call that reverses
        /// the decision.
        ///
        /// It is also why the reason is written by a person rather than picked from a list.
        /// Whoever types it should know a customer will read it.
        /// </remarks>
        public static EmailMessage Rejected(SiteModel site, AccountModel account, string reason) =>
            new(site.SiteKey, account.Email, account.ContactName,
                $"About your trade account application — {site.Name}",
                $"We are not able to open a trade account for {account.Company} at the moment.\n\n" +
                $"{reason}\n\n" +
                "If that looks wrong, or something has changed, reply to this message and we " +
                "will take another look.\n\n" +
                $"— {site.Name}");
    }
}
