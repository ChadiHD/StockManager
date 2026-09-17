using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;

namespace StockApi.Feeds
{
    /// <summary>
    /// Pulls every store's distributor feeds once a night, unattended.
    /// </summary>
    /// <remarks>
    /// The exit criterion for T4. Until this existed, a feed synced only when somebody opened
    /// the admin portal and pressed a button, which meant stock quantities were as fresh as
    /// the last time an operator happened to think about it.
    ///
    /// It lives in StockApi rather than SMStore because StockApi holds the Data Protection ring
    /// that decrypts <c>DistributorFeed.SecretRef</c>, and next to
    /// <see cref="ProductImageBackgroundService"/> because they are the same kind of thing: out
    /// of band work that must not fail a request.
    ///
    /// Two hosts could run this at once — Azure Container Apps scales by replica count, and
    /// each replica holds its own copy. That is not this class's problem to solve: every feed
    /// is claimed before it is fetched, so a second replica waking at the same hour finds the
    /// feeds taken and records nothing. See spDistributorFeed_ClaimForSync.
    /// </remarks>
    public class DistributorFeedSyncBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _config;
        private readonly ILogger<DistributorFeedSyncBackgroundService> _logger;

        public DistributorFeedSyncBackgroundService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<DistributorFeedSyncBackgroundService> logger)
        {
            _services = services;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            /*
            Off unless asked for, like Icecat:EnrichmentEnabled.

            A developer pressing F5 on the app host should not open an SFTP session to a real
            distributor with real credentials. The feeds in a development database are usually
            the production ones — that is what makes them worth testing against — so the default
            has to be the safe one and turning it on has to be a decision.
            */
            if (!_config.GetValue("Feeds:SyncEnabled", false))
            {
                _logger.LogInformation(
                    "Scheduled distributor feed sync is disabled (Feeds:SyncEnabled). Feeds can " +
                    "still be synced on demand from the admin portal.");
                return;
            }

            if (!TryReadSyncTime(out var syncTime))
            {
                // Refused rather than defaulted. A misread time would sync at midnight for ever
                // while the configuration file plainly said otherwise, and nothing would explain
                // the difference.
                return;
            }

            _logger.LogInformation(
                "Scheduled distributor feed sync is on, at {SyncTime} UTC daily.", syncTime);

            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = UntilNext(syncTime, DateTime.UtcNow);

                _logger.LogInformation("Next distributor feed sync in {Wait}.", wait);

                try
                {
                    await Task.Delay(wait, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                await SyncEverySiteAsync(stoppingToken);
            }
        }

        /// <summary>
        /// One pass over every active store. Exposed for the tests, which have no host.
        /// </summary>
        public async Task SyncEverySiteAsync(CancellationToken stoppingToken)
        {
            List<SiteModel> sites;

            try
            {
                using var scope = _services.CreateScope();
                var siteData = scope.ServiceProvider.GetRequiredService<ISiteData>();

                // Active only. An inactive site is one whose storefront answers nothing, and
                // importing its distributor's catalog nightly would be paying a distributor's
                // rate limit for rows no customer can see.
                sites = siteData.GetSites().Where(site => site.IsActive).ToList();
            }
            catch (Exception exception)
            {
                // A database that cannot be read now is one that may be readable tomorrow, so
                // this returns to the wait rather than ending the service.
                _logger.LogError(exception,
                    "Could not read the site list, so no feeds were synced tonight.");
                return;
            }

            foreach (var site in sites)
            {
                if (stoppingToken.IsCancellationRequested) return;

                /*
                Per site, inside its own scope and its own try.

                One store's distributor being unreachable must not stop another store's sync —
                they are separate businesses and the platform's whole promise is that they do
                not share fate. DistributorFeedSyncService already captures a failure per feed
                rather than throwing, so this catch is for what it cannot: a scope that will
                not build, or a site whose feed list cannot be read at all.
                */
                try
                {
                    using var scope = _services.CreateScope();
                    var sync = scope.ServiceProvider.GetRequiredService<IDistributorFeedSyncService>();

                    var results = await sync.SyncAllAsync(site.Id, FeedSyncTrigger.Schedule);

                    LogOutcome(site, results);

                    // Only from here, never from the controller: an operator who pressed Sync is
                    // already reading the answer, and mailing them about it is how a channel
                    // stops being believed. FeedAlertService never throws.
                    await scope.ServiceProvider.GetRequiredService<FeedAlertService>()
                        .ReportAsync(site, results, stoppingToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception,
                        "Scheduled feed sync failed for site {SiteKey}.", site.SiteKey);
                }
            }
        }

        private void LogOutcome(SiteModel site, List<DistributorFeedResult> results)
        {
            if (results.Count == 0)
            {
                // Not a warning. Most stores on this platform will never configure a feed, and
                // a nightly warning about each of them is how the log stops being read.
                _logger.LogInformation("Site {SiteKey} has no enabled feeds.", site.SiteKey);
                return;
            }

            var failed = results.Count(result => !result.Succeeded && !result.AlreadyRunning);

            if (failed > 0)
            {
                // The alert is item 5's; this is the operator-facing record until then, and it
                // stays afterwards because a log is where a failure at 02:00 is reconstructed.
                _logger.LogWarning(
                    "Scheduled feed sync for site {SiteKey}: {Failed} of {Total} feeds failed.",
                    site.SiteKey, failed, results.Count);
                return;
            }

            _logger.LogInformation(
                "Scheduled feed sync for site {SiteKey}: {Total} feeds, {Imported} products imported.",
                site.SiteKey, results.Count, results.Sum(result => result.Imported));
        }

        /// <summary>
        /// Reads <c>Feeds:SyncAtUtc</c>, which is a time of day and nothing cleverer.
        /// </summary>
        /// <remarks>
        /// UTC, and deliberately not per site. A store's right hour is whenever its distributor
        /// finishes dropping the file, which the platform has no way to know and cannot infer
        /// from the store's country — so one value, documented, and a per-feed column when a
        /// second distributor actually wants one.
        ///
        /// Not a cron expression either. Cron would buy "twice a day" and "weekdays only" at
        /// the cost of a dependency and a syntax to get wrong, and nothing has asked for either.
        /// </remarks>
        public static bool TryReadSyncTime(IConfiguration config, ILogger logger, out TimeSpan syncTime)
        {
            const string key = "Feeds:SyncAtUtc";
            const string fallback = "02:00";

            var configured = config.GetValue<string?>(key);

            if (string.IsNullOrWhiteSpace(configured))
            {
                syncTime = TimeSpan.Parse(fallback);
                return true;
            }

            if (TimeSpan.TryParse(configured, out syncTime)
                && syncTime >= TimeSpan.Zero
                && syncTime < TimeSpan.FromDays(1))
            {
                return true;
            }

            logger.LogError(
                "{Key} is '{Configured}', which is not a time of day between 00:00 and 23:59. " +
                "No feeds will be synced on a schedule until it is corrected.", key, configured);

            syncTime = default;
            return false;
        }

        private bool TryReadSyncTime(out TimeSpan syncTime) =>
            TryReadSyncTime(_config, _logger, out syncTime);

        /// <summary>How long from <paramref name="now"/> until the next run.</summary>
        /// <remarks>
        /// Today's slot if it has not passed, otherwise tomorrow's. A host that starts after the
        /// hour therefore waits a day rather than syncing immediately, which is the right way
        /// round: a restart loop would otherwise re-import every feed on every restart, and a
        /// missed night is what the staleness alert exists to surface.
        /// </remarks>
        public static TimeSpan UntilNext(TimeSpan syncTime, DateTime nowUtc)
        {
            var next = nowUtc.Date + syncTime;

            if (next <= nowUtc)
            {
                next = next.AddDays(1);
            }

            return next - nowUtc;
        }
    }
}
