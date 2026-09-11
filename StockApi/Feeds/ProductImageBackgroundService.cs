using SMDataManager.Library.Feeds;

namespace StockApi.Feeds
{
    // Fills in missing product images, a batch at a time.
    //
    // Runs out-of-band on purpose: a feed sync imports thousands of products, and one Icecat
    // call per product inside that request would take minutes and time the caller out.
    //
    // A finished sync signals this service through IImageEnrichmentSignal, so pressing Sync
    // starts enrichment straight away instead of leaving new products to wait out the idle
    // interval. Between batches it pauses only briefly, so a sync's backlog drains in minutes
    // rather than over hours; once nothing is left it sleeps until the next signal.
    public class ProductImageBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IImageEnrichmentSignal _signal;
        private readonly IConfiguration _config;
        private readonly ILogger<ProductImageBackgroundService> _logger;

        public ProductImageBackgroundService(
            IServiceProvider services,
            IImageEnrichmentSignal signal,
            IConfiguration config,
            ILogger<ProductImageBackgroundService> logger)
        {
            _services = services;
            _signal = signal;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue("Icecat:EnrichmentEnabled", false))
            {
                _logger.LogInformation(
                    "Product image enrichment is disabled (Icecat:EnrichmentEnabled). Images can still be " +
                    "filled in on demand from the products page.");
                return;
            }

            var batchSize = _config.GetValue("Icecat:BatchSize", 50);

            // Between batches of an active backlog. Short enough to drain a sync quickly, long
            // enough not to hammer Icecat.
            var betweenBatches = TimeSpan.FromSeconds(_config.GetValue("Icecat:BatchPauseSeconds", 2));

            // Fallback sweep when no sync has signalled — catches products added by any route
            // other than a feed import.
            var idleInterval = TimeSpan.FromHours(_config.GetValue("Icecat:IdleHours", 6));

            // Let the app finish starting before doing outbound work.
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                bool workRemains = false;

                try
                {
                    using var scope = _services.CreateScope();
                    var enricher = scope.ServiceProvider.GetRequiredService<IProductImageEnricher>();

                    var result = await enricher.EnrichAsync(batchSize, stoppingToken);

                    // Considered counts candidates the batch picked up. Zero means every
                    // product either has an image or has been tried, so there is nothing to
                    // come back for until something changes.
                    workRemains = result.Enabled && result.Considered > 0;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Product image enrichment pass failed; will retry.");
                }

                try
                {
                    if (workRemains)
                    {
                        await Task.Delay(betweenBatches, stoppingToken);
                    }
                    else
                    {
                        // Whichever comes first: a sync asking for a pass, or the periodic
                        // sweep. WhenAny leaves the loser running, which is fine — the delay
                        // is abandoned and the signal stays pending for the next iteration.
                        await Task.WhenAny(
                            _signal.WaitForRequestAsync(stoppingToken),
                            Task.Delay(idleInterval, stoppingToken));
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
