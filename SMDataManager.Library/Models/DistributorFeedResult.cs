using System.Collections.Generic;

namespace SMDataManager.Library.Models
{
    public class DistributorFeedResult
    {
        public string Distributor { get; set; }
        public string SourceFileName { get; set; }
        public int RecordCount { get; set; }
        public int Imported { get; set; }

        /// <summary>Products this distributor no longer lists, flagged rather than deleted.</summary>
        public int Delisted { get; set; }

        public bool Succeeded { get; set; }

        /// <summary>
        /// Nothing ran because a sync of this feed was already in progress.
        /// </summary>
        /// <remarks>
        /// Distinct from a failure, and the distinction is the whole point. Once T4 runs feeds
        /// on a schedule, an operator pressing Sync during the nightly window is the ordinary
        /// case — reporting it as a failure would put a red banner in front of somebody who did
        /// nothing wrong, and would teach them to ignore the banner that matters.
        ///
        /// <see cref="Succeeded"/> is still false: no import happened. Callers deciding whether
        /// to shout must test both.
        /// </remarks>
        public bool AlreadyRunning { get; set; }

        public string Error { get; set; }

        /// <summary>
        /// Every field name the feed actually contains, with how well populated it is and an
        /// example value. Filled by a test run only.
        ///
        /// Mapping a distributor is otherwise guesswork: an unmapped or misspelled field name
        /// silently yields NULL for every row, the sync still reports success, and the gap
        /// only surfaces much later as an empty facet or a missing image. This is the list to
        /// pick names from.
        /// </summary>
        public List<FeedFieldSample> DiscoveredFields { get; set; } = new List<FeedFieldSample>();
    }

    /// <summary>One field found in a feed, and how usable it looks.</summary>
    public class FeedFieldSample
    {
        public string Name { get; set; }

        /// <summary>
        /// Share of sampled records where this field has a value, 0-100. A field present on
        /// every row is a candidate for an identity mapping; one present on a handful is not.
        /// </summary>
        public int PopulatedPct { get; set; }

        public string SampleValue { get; set; }
    }

    public class FeedUpsertResult
    {
        public int Received { get; set; }
        public int Delisted { get; set; }
    }

    // One product row parsed out of a distributor feed.
    public class DistributorFeedRecord
    {
        public string DistributorSku { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public decimal? Cost { get; set; }
        public decimal? Srp { get; set; }
        public int Quantity { get; set; }

        public string Manufacturer { get; set; }
        public string Mpn { get; set; }
        public string Ean { get; set; }

        /// <summary>True when the feed indicates Icecat content exists for this product.</summary>
        public bool? IcecatAvailable { get; set; }

        /// <summary>Every flattened key/value from the source record, for diagnostics.</summary>
        public Dictionary<string, string> Raw { get; set; } = new();
    }
}
