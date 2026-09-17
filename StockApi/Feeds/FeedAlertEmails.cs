using SMDataManager.Library.Models;
using StockManager.Notifications;

namespace StockApi.Feeds
{
    /// <summary>
    /// The copy for the two things an operator needs telling about a feed.
    /// </summary>
    /// <remarks>
    /// Written here rather than in a template for the same reason
    /// <c>RegistrationEmails</c> and <c>PasswordResetEmails</c> are: T4 owns the call site and
    /// T6 owns the per-site templates, so this file is what T6 replaces.
    ///
    /// Both messages point at the portal rather than carrying the diagnosis. The address is a
    /// configured back-office one and therefore trusted, but the body still passes through
    /// <c>LoggingEmailSender</c>, which outside Development no longer logs bodies at all — so a
    /// mail that tried to be the diagnosis would be the one copy of it, in the one place nobody
    /// can search.
    /// </remarks>
    public static class FeedAlertEmails
    {
        /// <summary>A scheduled sync that failed, and had not failed the time before.</summary>
        /// <remarks>
        /// Sent on the transition into failure, not on every failing run — see
        /// <see cref="FeedAlertService"/>. A feed broken for a week that mailed nightly would
        /// have the eighth message filtered, and the filter would still be there when the next
        /// real failure happened.
        /// </remarks>
        public static EmailMessage SyncFailed(
            SiteModel site, string feedName, string message) =>
            new(site.SiteKey, site.OperatorEmail, null,
                $"Distributor feed failed: {feedName} — {site.Name}",
                $"""
                 Tonight's scheduled sync of the {feedName} feed did not complete.

                 {message}

                 Stock quantities and costs from this distributor are as old as the last
                 successful sync. Nothing has been delisted — a failed sync changes no product
                 rows at all — but the catalog is not being updated.

                 The feeds page in the admin portal has the full history of attempts, including
                 how far this one got before it stopped.

                 You will not get another message about this feed until it succeeds and then
                 fails again.

                 — {site.Name}
                 """);

        /// <summary>
        /// Feeds that have not delivered inside the store's threshold, whether or not anything
        /// failed.
        /// </summary>
        /// <remarks>
        /// One message listing every stale feed rather than one per feed: staleness is usually
        /// caused by something upstream of any single feed — the scheduler off, the host down —
        /// so a message per feed would be several copies of one problem.
        /// </remarks>
        public static EmailMessage FeedsAreStale(
            SiteModel site, IReadOnlyCollection<string> feedNames) =>
            new(site.SiteKey, site.OperatorEmail, null,
                $"Distributor data is going stale — {site.Name}",
                $"""
                 These feeds have not delivered anything for longer than {site.Name} allows
                 ({site.FeedStaleAfterHours} hours):

                 {string.Join("\n", feedNames.Select(name => $"  - {name}"))}

                 {StaleConsequence(site)}

                 Nothing has failed, which is what makes this worth saying: if a sync had
                 failed you would have had a message about that instead. A feed that is simply
                 not being attempted usually means the scheduled sync is off or the host that
                 runs it has been down.

                 — {site.Name}
                 """);

        /// <summary>
        /// What staleness is actually doing to the storefront, which depends on a setting.
        /// </summary>
        /// <remarks>
        /// Worth the branch: "your products have disappeared" and "you are selling on figures
        /// nobody has confirmed" call for different urgency, and an operator who had to guess
        /// which one applied would have to go and read a column to find out.
        /// </remarks>
        private static string StaleConsequence(SiteModel site) =>
            site.HideStaleProducts
                ? "Those distributors' products are currently hidden from the storefront, "
                  + "because this store is set to hide stale stock."
                : "Those distributors' products are still on sale, at the quantities and costs "
                  + "from the last successful sync, because this store is not set to hide stale "
                  + "stock.";
    }
}
