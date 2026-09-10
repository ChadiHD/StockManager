using System;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// A catalog row as the storefront reads it: the product joined to the store's own
    /// category, already filtered by site and by customer-group visibility.
    /// </summary>
    public class CatalogItemModel
    {
        public int Id { get; set; }
        public string Sku { get; set; }
        public string ProductName { get; set; }
        public string Description { get; set; }

        /// <summary>List price before any group discount. <see cref="IPriceResolver"/> turns it into a sell price.</summary>
        public decimal RetailPrice { get; set; }

        /// <summary>
        /// Buy price. Present so the margin floor can be applied without a second read, and
        /// therefore must never be projected into anything a customer can see.
        /// </summary>
        public decimal? Cost { get; set; }

        public int QuantityInStock { get; set; }
        public string ProductImage { get; set; }
        public string Manufacturer { get; set; }
        public string ManufacturerPartNumber { get; set; }
        public string Ean { get; set; }
        public string Distributor { get; set; }
        public string Source { get; set; }
        public string Badge { get; set; }
        public bool Featured { get; set; }
        public DateTime? LastSynced { get; set; }

        public string CategorySlug { get; set; }
        public string CategoryName { get; set; }

        /// <summary>
        /// Rows matching the query before paging, repeated on every row by the window function
        /// in spCatalog_Search so the pager needs no second round trip.
        /// </summary>
        public int TotalCount { get; set; }
    }
}
