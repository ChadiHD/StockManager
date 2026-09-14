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
    /// Coalescing latch: several requests arriving before the worker looks produce one wake-up,
    /// because the worker drains the whole backlog anyway.
    /// </summary>
    /// <remarks>
    /// A SemaphoreSlim looks like the obvious fit and is the wrong one. The worker waits on this
    /// alongside an idle timer, so any time the timer wins, the losing WaitAsync stays queued on
    /// the semaphore. Release() hands the count to a queued waiter before incrementing the count,
    /// so the next RequestPass() completes that orphan — which nobody is observing — and the live
    /// waiter is never woken. Sync-starts-a-pass then works exactly once per process, and each
    /// subsequent idle period adds another orphan that must be absorbed first.
    ///
    /// A flag plus one replaceable TaskCompletionSource has no per-waiter state to strand: an
    /// abandoned wait leaves the flag set, so the request survives for whoever asks next.
    /// </remarks>
    public sealed class ImageEnrichmentSignal : IImageEnrichmentSignal
    {
        private readonly object _gate = new object();

        private bool _requested;
        private TaskCompletionSource<bool> _waiter;

        public void RequestPass()
        {
            TaskCompletionSource<bool> waiting;

            lock (_gate)
            {
                // Latched even when there is a waiter to hand the signal to directly. That
                // waiter may already have been abandoned, and nothing here can tell; leaving the
                // flag set costs one pass that finds no candidates, while clearing it on the
                // assumption someone is listening loses the request outright.
                _requested = true;
                waiting = _waiter;
                _waiter = null;
            }

            waiting?.TrySetResult(true);
        }

        public Task WaitForRequestAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> waiting;

            lock (_gate)
            {
                if (_requested)
                {
                    _requested = false;
                    return Task.CompletedTask;
                }

                _waiter ??= new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                waiting = _waiter;
            }

            return waiting.Task.WaitAsync(cancellationToken);
        }
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
