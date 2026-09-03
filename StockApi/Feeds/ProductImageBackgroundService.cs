using SMDataManager.Library.Feeds;

namespace StockApi.Feeds
{
    // Fills in missing product images in the background, a batch at a time.
    //
    // Runs out-of-band on purpose: a feed sync imports thousands of products, and one Icecat
    // call per product inside that request would take minutes and time the caller out. This
    // works through the backlog gradually instead, and stays idle once every product either has
    // an image or has been tried.
    public class ProductImageBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _config;
        private readonly ILogger<ProductImageBackgroundService> _logger;

        public ProductImageBackgroundService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<ProductImageBackgroundService> logger)
        {
            _services = services;
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
            var interval = TimeSpan.FromMinutes(_config.GetValue("Icecat:IntervalMinutes", 15));
            var idleInterval = TimeSpan.FromHours(6);

            // Let the app finish starting before doing outbound work.
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = interval;

                try
                {
                    using var scope = _services.CreateScope();
                    var enricher = scope.ServiceProvider.GetRequiredService<IProductImageEnricher>();

                    var result = await enricher.EnrichAsync(batchSize, stoppingToken);

                    if (!result.Enabled || result.Considered == 0)
                    {
                        // Nothing left to do — back off rather than polling every few minutes.
                        wait = idleInterval;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Product image enrichment pass failed; will retry.");
                }

                try { await Task.Delay(wait, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
