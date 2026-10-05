using SMDataManager.Library.DataAccess;

namespace StockApi.Scheduling
{
    /// <summary>
    /// Once a day, deletes abandoned anonymous baskets and old sent mail.
    /// </summary>
    /// <remarks>
    /// T5 deferred the basket sweep here and T6 the outbox's, both "with the hosting decision".
    /// T7 made it: scheduled work stays in this host, which never scales to zero, beside the
    /// feed sync and the expiry sweep.
    ///
    /// **On by default**, unlike those two. It writes to nobody and opens no connection to
    /// anybody else's server; it deletes an anonymous basket nobody has touched in a month and
    /// mail that was delivered three months ago. Off by default would mean a development
    /// database that grows with every E2E run and a production one that grows for ever until
    /// someone remembers this setting.
    ///
    /// Nothing guards against two replicas. The deletes are idempotent: the second finds the
    /// rows already gone.
    /// </remarks>
    public class HousekeepingBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _config;
        private readonly ILogger<HousekeepingBackgroundService> _logger;

        public HousekeepingBackgroundService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<HousekeepingBackgroundService> logger)
        {
            _services = services;
            _config = config;
            _logger = logger;
        }

        public int AbandonedBasketDays => _config.GetValue("Housekeeping:AbandonedBasketDays", 30);

        public int SentMailDays => _config.GetValue("Housekeeping:SentMailDays", 90);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue("Housekeeping:Enabled", true))
            {
                _logger.LogInformation(
                    "Housekeeping is disabled (Housekeeping:Enabled). Abandoned baskets and sent " +
                    "mail are kept indefinitely.");
                return;
            }

            if (!DailySchedule.TryRead(_config, "Housekeeping:AtUtc", "03:30", _logger, out var runAt))
            {
                return;
            }

            _logger.LogInformation(
                "Housekeeping is on, at {RunAt} UTC daily: anonymous baskets after {BasketDays} " +
                "day(s), sent mail after {MailDays} day(s).", runAt, AbandonedBasketDays, SentMailDays);

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

                Sweep(DateTime.UtcNow);
            }
        }

        /// <summary>One pass. Exposed for the tests, which have no host.</summary>
        public void Sweep(DateTime nowUtc)
        {
            // A day is the floor, not a nicety. Zero would delete the basket a visitor is
            // filling right now, and a negative number would delete every anonymous basket there
            // is — the setting is read at run time, so a typo would do it tonight.
            if (AbandonedBasketDays < 1 || SentMailDays < 1)
            {
                _logger.LogError(
                    "Housekeeping:AbandonedBasketDays is {BasketDays} and Housekeeping:SentMailDays " +
                    "is {MailDays}; both must be at least one day. Nothing was swept.",
                    AbandonedBasketDays, SentMailDays);
                return;
            }

            try
            {
                using var scope = _services.CreateScope();

                var swept = scope.ServiceProvider.GetRequiredService<IHousekeepingData>().Sweep(
                    nowUtc.AddDays(-AbandonedBasketDays),
                    nowUtc.AddDays(-SentMailDays));

                _logger.LogInformation(
                    "Housekeeping deleted {Baskets} abandoned basket(s) and {Mail} sent message(s).",
                    swept.AbandonedBaskets, swept.SentMail);
            }
            catch (Exception exception)
            {
                // Tomorrow may work. A BackgroundService that throws is gone for the life of the
                // process.
                _logger.LogError(exception, "Housekeeping failed; nothing further is swept until tomorrow.");
            }
        }
    }
}
