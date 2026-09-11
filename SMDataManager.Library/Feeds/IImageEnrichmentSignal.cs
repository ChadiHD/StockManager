using System.Threading;
using System.Threading.Tasks;

namespace SMDataManager.Library.Feeds
{
    /// <summary>
    /// Lets a finished feed sync wake the image enrichment worker.
    ///
    /// Enrichment stays out of the sync request itself — one Icecat call per product across a
    /// few thousand rows takes minutes and times the caller out. But the worker backs off to a
    /// long idle wait once the backlog is clear, so without a nudge a sync's new products can
    /// sit unenriched for hours. This is the nudge.
    /// </summary>
    public interface IImageEnrichmentSignal
    {
        /// <summary>Ask the worker to start a pass now. Never blocks; safe to call repeatedly.</summary>
        void RequestPass();

        /// <summary>Completes when a pass has been requested.</summary>
        Task WaitForRequestAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Coalescing signal: several requests arriving while a pass is already pending produce
    /// one wake-up, because the worker drains the whole backlog anyway.
    /// </summary>
    public sealed class ImageEnrichmentSignal : IImageEnrichmentSignal
    {
        private readonly SemaphoreSlim _pending = new SemaphoreSlim(0, 1);

        public void RequestPass()
        {
            try
            {
                _pending.Release();
            }
            catch (SemaphoreFullException)
            {
                // A pass is already pending. Nothing to do — one wake-up covers both.
            }
        }

        public Task WaitForRequestAsync(CancellationToken cancellationToken) =>
            _pending.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// For hosts that import feeds but run no enrichment worker. Requests are dropped, and a
    /// wait never completes, so a caller that waits simply idles until shutdown.
    /// </summary>
    public sealed class NullImageEnrichmentSignal : IImageEnrichmentSignal
    {
        public void RequestPass()
        {
        }

        public Task WaitForRequestAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
