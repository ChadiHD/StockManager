namespace StockApi.Email
{
    /// <summary>
    /// Drains <c>dbo.EmailOutbox</c> for every store, every few seconds.
    /// </summary>
    /// <remarks>
    /// In <c>StockApi</c>, beside the feed scheduler, and nowhere else. The storefront queues
    /// and this host sends; two dispatchers would be safe — the claim sees to that — but one
    /// is all the volume needs and one is the easier thing to find when mail stops.
    ///
    /// **On by default, unlike the feed sync**, and the difference is the point. A feed sync
    /// opens a session to a real distributor with real credentials, so turning it on has to be
    /// a decision. This hands messages to <c>IEmailSender</c>, which is still the logger — on
    /// a developer's machine it sends nothing anywhere, and turning it off by default would
    /// silently stop the confirmation link appearing in the dashboard, which is how the
    /// registration journey is tested by hand. When a real transport is configured, that
    /// configuration is the decision.
    /// </remarks>
    public class EmailDispatchBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _config;
        private readonly ILogger<EmailDispatchBackgroundService> _logger;

        public EmailDispatchBackgroundService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<EmailDispatchBackgroundService> logger)
        {
            _services = services;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue("Email:DispatchEnabled", true))
            {
                _logger.LogWarning(
                    "Outbound mail dispatch is disabled (Email:DispatchEnabled). Messages are " +
                    "being queued and nothing is sending them.");
                return;
            }

            var seconds = _config.GetValue("Email:DispatchIntervalSeconds", 5);

            if (seconds < 1)
            {
                // Refused rather than clamped: a zero here is a loop that hammers the database,
                // and quietly picking another number would hide that somebody asked for it.
                _logger.LogError(
                    "Email:DispatchIntervalSeconds is {Seconds}, which is not a positive number " +
                    "of seconds. No mail will be dispatched until it is corrected.", seconds);
                return;
            }

            var interval = TimeSpan.FromSeconds(seconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                var claimed = 0;

                try
                {
                    using var scope = _services.CreateScope();

                    claimed = await scope.ServiceProvider.GetRequiredService<EmailDispatcher>()
                        .DispatchBatchAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // The database may be back on the next pass; ending the service would stop
                    // mail for the life of the process with one log line to say so.
                    _logger.LogError(exception, "An outbound mail dispatch pass failed.");
                }

                // A full batch means more are probably waiting, so go again straight away
                // rather than letting a backlog drain at twenty messages per interval.
                if (claimed >= EmailDispatcher.BatchSize)
                {
                    continue;
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
