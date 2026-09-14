using System;

namespace SMDataManager.Library.Pricing
{
    /// <summary>
    /// What a given account pays for a given product.
    /// </summary>
    public class ResolvedPrice
    {
        public decimal ListPrice { get; set; }

        /// <summary>What the account pays, after the group discount and the margin floor.</summary>
        public decimal NetPrice { get; set; }

        /// <summary>
        /// The discount actually granted, recomputed from <see cref="NetPrice"/>. Differs from
        /// the group's configured rate whenever the floor bit, which is what the customer and
        /// the sales team both need to see.
        /// </summary>
        public decimal EffectiveDiscountPct { get; set; }

        /// <summary>True when the margin floor, not the group rate, set the price.</summary>
        public bool FloorApplied { get; set; }
    }

    /// <summary>
    /// Turns a list price into a sell price for one account.
    ///
    /// Deliberately one implementation shared by the storefront and by admin quote pricing.
    /// Two implementations of this rule would drift, and the first anyone would hear of it is
    /// a customer quoted one price on the catalog and another on their quote.
    /// </summary>
    public interface IPriceResolver
    {
        /// <param name="listPrice">dbo.Product.RetailPrice.</param>
        /// <param name="cost">dbo.Product.Cost, or null when unknown — the floor is then skipped.</param>
        /// <param name="groupDiscountPct">dbo.CustomerGroup.Discount, or 0 for no group.</param>
        /// <param name="minMarginPct">dbo.Site.MinMarginPct, or 0 to disable the floor.</param>
        ResolvedPrice Resolve(decimal listPrice, decimal? cost, decimal groupDiscountPct, decimal minMarginPct);
    }
}
