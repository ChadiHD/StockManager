using System.Collections.Generic;

namespace SMDataManager.Library.Models
{
    // Runtime settings for a single feed connection, projected from the dbo.DistributorFeed row
    // by DistributorFeedModel.ToSettings once the credential has been resolved from its store.
    // Feeds are no longer configured in appsettings — they are managed from the admin portal.
    public class DistributorFeedSettings
    {
        /// <summary>Distributor name stored on dbo.Product.Distributor.</summary>
        public string Name { get; set; }

        public string Host { get; set; }
        public int Port { get; set; } = 22;
        public string Username { get; set; }
        public string Password { get; set; }

        /// <summary>Remote directory to search for XML feeds; searched recursively.</summary>
        public string RemoteDirectory { get; set; } = ".";

        /// <summary>
        /// Base64 SHA-256 of the expected host key. When set, a mismatching server is rejected.
        /// Leave null only for trusted networks — without it the host is not verified.
        /// </summary>
        public string HostKeySha256 { get; set; }

        /// <summary>Field names in the feed XML mapped onto product columns.</summary>
        public FeedFieldMap Fields { get; set; } = new();

        public bool Enabled { get; set; } = true;
    }

    // The feed flattens each XML record into name/value pairs; these settings say which of
    // those keys carry the values we store, so a new distributor is a config change rather
    // than a code change. Defaults match the FlexIT feed layout.
    public class FeedFieldMap
    {
        /// <summary>Distributor's own part number — the key a feed re-import matches on.</summary>
        public string Sku { get; set; } = "FlexITPartNumber";

        /// <summary>In the FlexIT feed the short product title lives in "Description".</summary>
        public string Name { get; set; } = "Description";

        /// <summary>Longer marketing copy, when the feed carries it.</summary>
        public string Description { get; set; } = "WebDescription";

        public string Category { get; set; } = "Category";

        /// <summary>What the distributor charges us — stored as Product.Cost.</summary>
        public string Cost { get; set; } = "SalesPrice";

        /// <summary>Suggested retail price; seeds RetailPrice on first import only.</summary>
        public string Srp { get; set; } = "SRP";

        public string Quantity { get; set; } = "StockQuantity";

        // ---- Identity, used to resolve product content (images) from Icecat ----

        public string Manufacturer { get; set; } = "Manufacturer";

        /// <summary>Manufacturer part number. Present on every row of the FlexIT feed.</summary>
        public string Mpn { get; set; } = "ManufacturerPartNumber";

        /// <summary>GTIN/EAN. Only a minority of rows carry one, so it is the fallback key.</summary>
        public string Ean { get; set; } = "EAN";

        /// <summary>
        /// The feed's "IceCatID" column, which despite the name contains Yes/empty rather than an
        /// identifier. Kept as a hint for which products to try first.
        /// </summary>
        public string IcecatFlag { get; set; } = "IceCatID";
    }
}
