using System;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// One recorded attempt to sync a distributor feed.
    /// </summary>
    /// <remarks>
    /// The history <c>DistributorFeed.LastSyncStatus</c> cannot keep: that column holds one
    /// string and the next run overwrites it, so a feed broken every night for a week looks
    /// exactly like one broken once. Two things depend on this being kept — an operator
    /// answering "since when", and the failure alert deciding whether a failure is new.
    /// </remarks>
    public class FeedSyncLogModel
    {
        public int Id { get; set; }
        public int FeedId { get; set; }
        public int SiteId { get; set; }

        /// <summary>Only populated by the cross-feed read; per-feed history already knows.</summary>
        public string FeedName { get; set; }

        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public bool Succeeded { get; set; }

        public int RecordCount { get; set; }
        public int Imported { get; set; }
        public int Delisted { get; set; }

        /// <summary>The outcome, or a trimmed failure message. Never a whole exception.</summary>
        public string Message { get; set; }

        /// <summary>
        /// <see cref="FeedSyncTrigger"/> as stored. A feed that only ever succeeds when
        /// somebody presses the button is a scheduler problem, not a feed problem.
        /// </summary>
        public string TriggeredBy { get; set; }
    }

    /// <summary>
    /// What a finished sync has to say about itself, on the way to being recorded.
    /// </summary>
    /// <remarks>
    /// A parameter object rather than eight arguments, because six of them are ints and a
    /// transposed pair would record a plausible lie that nothing would ever flag.
    /// </remarks>
    public class FeedSyncRecord
    {
        /// <summary>When the attempt began, not when it was recorded.</summary>
        /// <remarks>
        /// Captured by the caller before the fetch. The procedure stamps the finish itself, so
        /// the pair spans the real duration — which is the number an operator looks at when a
        /// feed starts taking twice as long as it used to.
        /// </remarks>
        public DateTime StartedUtc { get; set; }

        public bool Succeeded { get; set; }

        /// <summary>The same text that lands in <c>LastSyncStatus</c>.</summary>
        public string Status { get; set; }

        public int RecordCount { get; set; }
        public int Imported { get; set; }
        public int Delisted { get; set; }

        public FeedSyncTrigger TriggeredBy { get; set; }
    }

    /// <summary>What started a sync.</summary>
    /// <remarks>
    /// Passed in rather than inferred. The sync service cannot tell a scheduled run from an
    /// operator's click — both arrive as a method call — and guessing from, say, whether an
    /// HTTP context exists would couple the library to a host.
    /// </remarks>
    public enum FeedSyncTrigger
    {
        /// <summary>Somebody pressed Sync in the admin portal.</summary>
        Operator,

        /// <summary>The nightly background service.</summary>
        Schedule
    }
}
