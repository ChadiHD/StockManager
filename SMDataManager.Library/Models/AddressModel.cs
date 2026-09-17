using System;

namespace SMDataManager.Library.Models
{
    /// <summary>A billing or delivery address belonging to a trading account.</summary>
    public class AddressModel
    {
        public int Id { get; set; }
        public int AccountId { get; set; }

        /// <summary>"Billing" or "Shipping". Not editable once set — see spAddress_Update.</summary>
        public string Kind { get; set; }

        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string City { get; set; }

        /// <summary>County, state or province. Absent in plenty of addressing systems.</summary>
        public string Region { get; set; }

        /// <summary>Absent too: Ireland's Eircode adoption is still partial.</summary>
        public string PostCode { get; set; }

        /// <summary>ISO 3166-1 alpha-2, so it can be compared with the site's own country.</summary>
        public string Country { get; set; }

        public bool IsDefault { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
