namespace StockApi.Fiscal;

/// <summary>
/// Drains <c>dbo.FiscalDocument</c> for every store, every thirty seconds (T14).
/// </summary>
/// <remarks>
/// On by default, like the mail dispatcher: a store on <c>none</c> queues nothing, so there is
/// nothing to claim until a store chooses an issuer, and that choice is the decision. Two replicas
/// are safe, because the claim is.
/// </remarks>
public class FiscalDocumentBackgroundService(
    IServiceProvider services, IConfiguration config, ILogger<FiscalDocumentBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Fiscal:DispatchEnabled", true))
        {
            logger.LogWarning(
                "Fiscal document dispatch is disabled (Fiscal:DispatchEnabled). Documents are being queued and nothing is issuing them.");
            return;
        }

        var seconds = config.GetValue("Fiscal:DispatchIntervalSeconds", 30);

        if (seconds < 1)
        {
            // Refused rather than clamped, for EmailDispatchBackgroundService's reason.
            logger.LogError(
                "Fiscal:DispatchIntervalSeconds is {Seconds}, which is not a positive number of seconds. " +
                "No fiscal documents will be issued until it is corrected.", seconds);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;

            try
            {
                using var scope = services.CreateScope();

                claimed = await scope.ServiceProvider.GetRequiredService<FiscalDocumentDispatcher>()
                    .DispatchBatchAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "A fiscal document dispatch pass failed.");
            }

            // A full batch means more are probably waiting.
            if (claimed >= FiscalDocumentDispatcher.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
