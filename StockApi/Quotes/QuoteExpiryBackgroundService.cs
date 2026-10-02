using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Scheduling;

namespace StockApi.Quotes
{
    /// <summary>
    /// Once a day, queues "your quote expires soon" for every store's unanswered priced quotes.
    /// </summary>
    /// <remarks>
    /// The one message triggered by nothing happening. Every other one is queued by the
    /// transition that causes it; this one needs a clock, and the repository already had the
    /// argument about where a clock lives: the abandoned-basket sweep was deferred to T7 with
    /// the hosting decision, because writing it early would have produced a procedure nothing
    /// calls. <see cref="Feeds.DistributorFeedSyncBackgroundService"/> is the counter-example —
    /// a scheduled job that does run, here, off by default — and this sits beside it. If T7
    /// decides scheduled work belongs elsewhere, the two move together.
    ///
    /// **Off by default** (<c>Quotes:ExpiryNoticeEnabled</c>). This is the only thing in the
    /// platform that writes to customers without anybody having done anything, and a
    /// development database is usually a copy of a real one. Turning it on queues a message to
    /// every real customer with a quote about to lapse; that should be a decision.
    ///
    /// Nothing guards against two replicas: <c>spQuote_QueueExpiryNotices</c> claims each quote
    /// with one UPDATE, so the second replica finds them stamped and queues nothing.
    /// </remarks>
    public class QuoteExpiryBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _config;
        private readonly ILogger<QuoteExpiryBackgroundService> _logger;

        public QuoteExpiryBackgroundService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<QuoteExpiryBackgroundService> logger)
        {
            _services = services;
            _config = config;
            _logger = logger;
        }

        /// <summary>How far ahead a quote's expiry counts as soon. Three days unless configured.</summary>
        /// <remarks>
        /// Long enough for a buyer to get the purchase approved internally, short enough that
        /// the reminder arrives when the decision is actually due rather than the day after the
        /// price.
        /// </remarks>
        public int WithinDays => _config.GetValue("Quotes:ExpiryNoticeDays", 3);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue("Quotes:ExpiryNoticeEnabled", false))
            {
                _logger.LogInformation(
                    "Quote expiry notices are disabled (Quotes:ExpiryNoticeEnabled). Customers " +
                    "are not reminded before a priced quote lapses.");
                return;
            }

            if (WithinDays < 1)
            {
                _logger.LogError(
                    "Quotes:ExpiryNoticeDays is {Days}, which is not a positive number of days. " +
                    "No expiry notices will be queued until it is corrected.", WithinDays);
                return;
            }

            if (!DailySchedule.TryRead(_config, "Quotes:ExpiryNoticeAtUtc", "07:00", _logger, out var runAt))
            {
                return;
            }

            _logger.LogInformation(
                "Quote expiry notices are on, at {RunAt} UTC daily, for quotes expiring within {Days} day(s).",
                runAt, WithinDays);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(DailySchedule.UntilNext(runAt, DateTime.UtcNow), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                SweepEverySite(stoppingToken);
            }
        }

        /// <summary>One pass over every active store. Exposed for the tests, which have no host.</summary>
        public void SweepEverySite(CancellationToken stoppingToken)
        {
            List<SiteModel> sites;

            try
            {
                using var scope = _services.CreateScope();

                // Active only, as for the feed sync: an inactive store's storefront answers
                // nothing, so a reminder would point a customer at a page that 404s.
                sites = scope.ServiceProvider.GetRequiredService<ISiteData>()
                    .GetSites().Where(site => site.IsActive).ToList();
            }
            catch (Exception exception)
            {
                // Tomorrow may be readable. A BackgroundService that throws is gone for the life
                // of the process.
                _logger.LogError(exception,
                    "Could not read the site list, so no quote expiry notices were queued today.");
                return;
            }

            foreach (var site in sites)
            {
                if (stoppingToken.IsCancellationRequested) return;

                // Per store, in its own scope and its own try, for the reason the feed sync
                // gives: separate businesses on one deployment do not share fate.
                try
                {
                    using var scope = _services.CreateScope();

                    var queued = scope.ServiceProvider.GetRequiredService<IQuoteData>()
                        .QueueExpiryNotices(site.Id, WithinDays);

                    _logger.LogInformation(
                        "Queued {Count} quote expiry notice(s) for site {SiteKey}.", queued, site.SiteKey);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception,
                        "Quote expiry notices failed for site {SiteKey}.", site.SiteKey);
                }
            }
        }
    }
}
