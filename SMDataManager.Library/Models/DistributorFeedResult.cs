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
        public string Error { get; set; }
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

        /// <summary>Every flattened key/value from the source record, for diagnostics.</summary>
        public Dictionary<string, string> Raw { get; set; } = new();
    }
}
