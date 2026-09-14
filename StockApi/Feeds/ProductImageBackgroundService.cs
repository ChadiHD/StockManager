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

            // Ceiling on the backoff that a wholly failed batch escalates to. Never shorter than
            // the batch pause, or a misconfigured value would reinstate the tight retry it exists
            // to prevent.
            var maxBackoff = TimeSpan.FromMinutes(_config.GetValue("Icecat:MaxBackoffMinutes", 5));
            if (maxBackoff < betweenBatches)
            {
                maxBackoff = betweenBatches;
            }

            // Fallback sweep when no sync has signalled — catches products added by any route
            // other than a feed import.
            var idleInterval = TimeSpan.FromHours(_config.GetValue("Icecat:IdleHours", 6));

            // Let the app finish starting before doing outbound work.
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }

            var backoff = betweenBatches;

            while (!stoppingToken.IsCancellationRequested)
            {
                bool workRemains = false;
                bool madeProgress = false;

                try
                {
                    using var scope = _services.CreateScope();
                    var enricher = scope.ServiceProvider.GetRequiredService<IProductImageEnricher>();

                    var result = await enricher.EnrichAsync(batchSize, stoppingToken);

                    // Considered counts candidates the batch picked up. Zero means every
                    // product either has an image or has been tried, so there is nothing to
                    // come back for until something changes.
                    workRemains = result.Enabled && result.Considered > 0;

                    // Only a lookup that reached a verdict stamps ImageLookupUtc, and only that
                    // drops a row out of the candidate set. A batch where every call threw
                    // leaves exactly the same rows waiting, so Considered alone would report
                    // work remaining forever and the loop would re-attempt them every
                    // betweenBatches — with the resilience handler's own retries on top, roughly
                    // a hundred requests a second at Icecat for as long as it is unreachable.
                    madeProgress = result.Failed < result.Considered;
                }
                // A cancelled stoppingToken is a real shutdown. An HttpClient timeout also
                // surfaces as OperationCanceledException with the token untouched, and must fall
                // through to the handler below — treating it as shutdown would end the worker on
                // one slow Icecat response, silently, until someone restarted the process.
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
                        if (madeProgress)
                        {
                            backoff = betweenBatches;
                        }
                        else
                        {
                            var doubled = backoff + backoff;
                            backoff = doubled > maxBackoff ? maxBackoff : doubled;

                            _logger.LogWarning(
                                "Every Icecat lookup in the batch failed, so no candidate was cleared. " +
                                "Next attempt in {Backoff}.", backoff);
                        }

                        await Task.Delay(backoff, stoppingToken);
                    }
                    else
                    {
                        backoff = betweenBatches;
                        await WaitForWorkAsync(idleInterval, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        // Waits for whichever comes first: a sync asking for a pass, or the idle sweep.
        //
        // The loser is cancelled rather than abandoned. An abandoned Task.Delay holds its timer
        // for the whole idle interval, and an abandoned wait on the signal holds a cancellation
        // registration against stoppingToken; both would accumulate one per iteration for the
        // life of the process.
        private async Task WaitForWorkAsync(TimeSpan idleInterval, CancellationToken stoppingToken)
        {
            using var loser = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            var signalled = _signal.WaitForRequestAsync(loser.Token);
            var swept = Task.Delay(idleInterval, loser.Token);

            await Task.WhenAny(signalled, swept);

            loser.Cancel();

            // Which one won does not matter — only that the wait is over. Awaiting both observes
            // the cancelled loser, which would otherwise surface as an unobserved task exception.
            try { await Task.WhenAll(signalled, swept); }
            catch (OperationCanceledException) { }

            stoppingToken.ThrowIfCancellationRequested();
        }
    }
}
