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

        /*
        Where the product sits on the store the admin is acting for, from
        dbo.fnSite_ProductPlacement -- the function the storefront reads -- so the list cannot
        claim a product is for sale that no customer can find. Empty on the POS's read, which
        has no store.
        */

        /// <summary>Whether this store sells it.</summary>
        public bool OnStore { get; set; }

        /// <summary>Why: Mapped, Shown, Hidden or Unmapped.</summary>
        public string Placement { get; set; }

        /// <summary>The store's category it is listed under, when it is on the store.</summary>
        public string StoreCategory { get; set; }

        /// <summary>The store's own choice: Show, Hide, or null to follow the mapping.</summary>
        public string Visibility { get; set; }

        /// <summary>The category a shown product was filed under by hand, if any.</summary>
        public int? OverrideCategoryId { get; set; }

        public bool Featured { get; set; }
        public string Badge { get; set; }
    }

    /// <summary>
    /// What became of a placement change: how many products it reached, or why it was refused.
    /// </summary>
    /// <remarks>
    /// A refusal is the procedure's own named THROW, carried as a message rather than an
    /// exception, so the portal can show the admin which rule they ran into.
    /// </remarks>
    public sealed record PlacementResult(int Matched, string Refusal)
    {
        public bool Saved => Refusal is null;
    }
}
