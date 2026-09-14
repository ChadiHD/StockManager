using System;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// Maps a distributor's brand string onto the name a content provider knows it by.
    /// </summary>
    public class BrandAliasModel
    {
        public int Id { get; set; }

        /// <summary>Null for an alias that applies whoever supplied the product.</summary>
        public string Distributor { get; set; }

        public string FeedBrand { get; set; }

        /// <summary>
        /// Null when the feed value is not a brand at all, which suppresses brand lookups for
        /// it rather than spending a request to be told no.
        /// </summary>
        public string IcecatBrand { get; set; }

        public string Note { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
