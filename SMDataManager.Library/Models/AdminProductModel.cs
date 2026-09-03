using System;

namespace SMDataManager.Library.Models
{
    // The catalog view of a product used by the admin portal. Kept separate from ProductModel
    // so the desktop POS binding surface stays exactly as it was — these columns are NULLable
    // in dbo.Product and are never set by the POS.
    public class AdminProductModel
    {
        public int Id { get; set; }
        public string ProductName { get; set; }
        public string Description { get; set; }
        public decimal RetailPrice { get; set; }
        public int QuantityInStock { get; set; }
        public bool IsTaxable { get; set; }
        public string ProductImage { get; set; }

        public string Sku { get; set; }
        public string Category { get; set; }
        public decimal? Cost { get; set; }
        public string Source { get; set; }
        public string Distributor { get; set; }
        public string DistributorSku { get; set; }
        public DateTime? LastSynced { get; set; }
        public bool Delisted { get; set; }

        public string Manufacturer { get; set; }
        public string ManufacturerPartNumber { get; set; }
        public string Ean { get; set; }
        public bool? IcecatAvailable { get; set; }
        public DateTime? ImageSourcedUtc { get; set; }
    }
}
