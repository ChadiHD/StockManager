using SMDataManager.Library.Models;

namespace StockApi.Feeds
{
    /// <summary>
    /// Reading a batch of sync results the same way everywhere that reports one.
    /// </summary>
    /// <remarks>
    /// Two endpoints answer with a list of <see cref="DistributorFeedResult"/> and both have to
    /// decide the same thing from it, so the rule lives here rather than being written twice
    /// with a chance of drifting. It drifted once already: both said "every result failed"
    /// before <see cref="DistributorFeedResult.AlreadyRunning"/> existed, and a feed that was
    /// merely busy would have been reported as a bad gateway.
    /// </remarks>
    public static class FeedSyncOutcome
    {
        /// <summary>
        /// Whether a batch is bad enough to answer with an error rather than a body.
        /// </summary>
        /// <remarks>
        /// A feed that was already running does not count against the batch. It is not a
        /// failure and it is not a success — no import happened, but nothing is wrong, and the
        /// run that holds the claim will report its own outcome.
        ///
        /// An empty list is not a failure either: a store with no feeds configured, or none
        /// enabled, has nothing to sync and asking it to is not an error.
        /// </remarks>
        public static bool IsTotalFailure(IReadOnlyCollection<DistributorFeedResult> results) =>
            results.Count > 0
            && results.All(result => !result.Succeeded && !result.AlreadyRunning);
    }
}
