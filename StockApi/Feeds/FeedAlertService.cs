using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockManager.Notifications;

namespace StockApi.Feeds
{
    /// <summary>
    /// Decides which feed problems are worth waking an operator for, and mails those.
    /// </summary>
    /// <remarks>
    /// The push half of "a failed sync is visible". The pull half is the portal, which shows
    /// every failure and every stale feed whenever somebody looks; this exists because nobody
    /// looks at a screen at 02:00 and a sync that fails silently every night is a sync nobody
    /// knows is broken.
    ///
    /// **Only the scheduler calls this.** An operator who pressed Sync is watching the toast,
    /// and mailing them about a failure they are already reading is how a channel stops being
    /// believed.
    /// </remarks>
    public class FeedAlertService
    {
        private readonly IDistributorFeedData _feeds;
        private readonly IEmailSender _sender;
        private readonly ILogger<FeedAlertService> _logger;

        public FeedAlertService(
            IDistributorFeedData feeds,
            IEmailSender sender,
            ILogger<FeedAlertService> logger)
        {
            _feeds = feeds;
            _sender = sender;
            _logger = logger;
        }

        /// <summary>
        /// Reports whatever this pass turned up for one store.
        /// </summary>
        /// <remarks>
        /// Never throws. An alert that failed to send must not cost the sync that triggered it,
        /// and by the time this runs the import has already committed — the same rule every
        /// other call site of <c>IEmailSender</c> follows.
        /// </remarks>
        public async Task ReportAsync(
            SiteModel site,
            IReadOnlyCollection<DistributorFeedResult> results,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var failedNow = await ReportFailuresAsync(site, results, cancellationToken);

                await ReportStalenessAsync(site, failedNow, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "Could not send feed alerts for site {SiteKey}.", site.SiteKey);
            }
        }

        /// <summary>Mails the feeds that just started failing. Returns every feed that failed.</summary>
        private async Task<HashSet<string>> ReportFailuresAsync(
            SiteModel site,
            IReadOnlyCollection<DistributorFeedResult> results,
            CancellationToken cancellationToken)
        {
            var failed = results
                .Where(result => !result.Succeeded && !result.AlreadyRunning)
                .ToList();

            var names = failed
                .Select(result => result.Distributor)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (failed.Count == 0)
            {
                return names;
            }

            if (!CanMail(site, $"{failed.Count} feed(s) failed"))
            {
                return names;
            }

            var feeds = _feeds.GetFeeds(site.Id);

            foreach (var result in failed)
            {
                var feed = feeds.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, result.Distributor, StringComparison.OrdinalIgnoreCase));

                if (feed is null)
                {
                    // A feed renamed or deleted between the sync and this pass. Nothing to read
                    // a history for, and the log line is the whole record.
                    _logger.LogWarning(
                        "Feed {Distributor} failed at {SiteKey} but no longer resolves; no alert sent.",
                        result.Distributor, site.SiteKey);
                    continue;
                }

                if (!IsNewFailure(feed.Id, site.Id))
                {
                    _logger.LogInformation(
                        "Feed {Distributor} at {SiteKey} failed again; no repeat alert.",
                        feed.Name, site.SiteKey);
                    continue;
                }

                await _sender.SendAsync(
                    FeedAlertEmails.SyncFailed(site, feed.Name, result.Error ?? "No detail was recorded."),
                    cancellationToken);

                _logger.LogInformation(
                    "Alerted {SiteKey} that feed {Distributor} has started failing.",
                    site.SiteKey, feed.Name);
            }

            return names;
        }

        /// <summary>
        /// Whether this failure is a change of state rather than a continuation.
        /// </summary>
        /// <remarks>
        /// The run that just finished is the first row of the history, so the question is what
        /// the second row says. A feed broken for a week that mailed nightly would have the
        /// eighth message filtered by the recipient, and the filter would still be in place
        /// when the next real failure happened.
        ///
        /// No history at all — a first-ever attempt — counts as new. So does a history this
        /// cannot read: erring towards one extra message is better than erring towards silence,
        /// which is the failure mode this whole class exists to prevent.
        /// </remarks>
        private bool IsNewFailure(int feedId, int siteId)
        {
            try
            {
                var history = _feeds.GetSyncHistory(feedId, siteId, take: 2);

                return history.Count < 2 || history[1].Succeeded;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "Could not read the sync history for feed {FeedId}; treating the failure as new.",
                    feedId);

                return true;
            }
        }

        /// <summary>
        /// Mails the feeds that have gone quiet, excluding any this pass already reported.
        /// </summary>
        /// <remarks>
        /// <paramref name="alreadyReported"/> is every feed that failed in this pass, not only
        /// the ones that were mailed. A feed failing for the third night is deliberately silent
        /// on the failure path, and letting the staleness path mail it instead would undo that.
        /// </remarks>
        private async Task ReportStalenessAsync(
            SiteModel site,
            HashSet<string> alreadyReported,
            CancellationToken cancellationToken)
        {
            var stale = _feeds.GetStaleFeeds(site.Id)
                .Select(feed => feed.Name)
                .Where(name => !alreadyReported.Contains(name))
                .ToList();

            if (stale.Count == 0)
            {
                return;
            }

            if (!CanMail(site, $"{stale.Count} feed(s) are stale"))
            {
                return;
            }

            await _sender.SendAsync(FeedAlertEmails.FeedsAreStale(site, stale), cancellationToken);

            _logger.LogInformation(
                "Alerted {SiteKey} that {Count} feed(s) have gone stale.", site.SiteKey, stale.Count);
        }

        /// <summary>
        /// Whether there is anywhere to send, logging the finding when there is not.
        /// </summary>
        /// <remarks>
        /// A store with no <c>OperatorEmail</c> still gets the problem written down, at Warning,
        /// because the alternative is a platform that silently decides an unconfigured store
        /// does not need to know its catalog has stopped updating. There is deliberately no
        /// platform-wide fallback address: the message names this store's distributor.
        /// </remarks>
        private bool CanMail(SiteModel site, string finding)
        {
            if (!string.IsNullOrWhiteSpace(site.OperatorEmail))
            {
                return true;
            }

            _logger.LogWarning(
                "Site {SiteKey}: {Finding}, and no OperatorEmail is set, so nobody was mailed.",
                site.SiteKey, finding);

            return false;
        }
    }
}
