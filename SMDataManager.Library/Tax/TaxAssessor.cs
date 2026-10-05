using SMDataManager.Library.Models;

namespace SMDataManager.Library.Tax
{
    /// <summary>
    /// Turns a store and a customer into the tax treatment their order carries.
    /// </summary>
    /// <remarks>
    /// Two callers raise an order — an admin converting a quote through <c>StockApi</c> and a
    /// customer accepting their own through <c>SMStore</c> — and this is the mapping from a
    /// <see cref="SiteModel"/> and an <see cref="AccountModel"/> to a <see cref="TaxContext"/>
    /// that both would otherwise write out separately. It is three lines, and it is the three
    /// lines most worth having in one place: written twice, the two paths eventually disagree
    /// about the same sale.
    /// </remarks>
    public sealed class TaxAssessor
    {
        private readonly TaxRuleSetProvider _ruleSets;

        public TaxAssessor(TaxRuleSetProvider ruleSets)
        {
            _ruleSets = ruleSets;
        }

        /// <summary>
        /// The treatment for a whole order.
        /// </summary>
        /// <remarks>
        /// Assessed as though every product were taxable, deliberately. Per-product exemption
        /// is <c>Product.IsTaxable</c>, and the flag lives beside the line in the database, so
        /// <c>spOrder_ConvertFromQuote</c> applies this rate only to the lines that carry it.
        /// Doing it here instead would mean reading the catalog back to find out.
        ///
        /// The treatment is the customer's — domestic, reverse charge, export — and does not
        /// vary line by line. Only whether the rate reaches a given line does.
        /// </remarks>
        public TaxAssessment ForOrder(SiteModel site, AccountModel account) =>
            _ruleSets.For(site.TaxRuleSet).Assess(new TaxContext(
                site.Country,
                site.StandardTaxRatePct,
                account?.Country,
                account?.VatNumber,
                IsTaxable: true));
    }
}
