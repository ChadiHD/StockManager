using System;

namespace SMDataManager.Library.Tax
{
    /// <summary>
    /// Everything a rule set is allowed to decide from.
    /// </summary>
    /// <remarks>
    /// Values rather than entities, deliberately, and for the reason
    /// <see cref="Pricing.IPriceResolver"/> takes values: this library is shared by both hosts
    /// and neither an <c>ISiteContext</c> nor an HTTP request exists down here. It also means
    /// the whole decision table can be driven from a test matrix with no database.
    /// </remarks>
    /// <param name="SiteCountry">The store's own country, ISO 3166-1 alpha-2.</param>
    /// <param name="StandardRatePct">The store's standard rate, as a percentage.</param>
    /// <param name="CustomerCountry">
    /// The account's country. Free text on <c>dbo.Account</c> — a registration form collected
    /// it — so a rule set has to cope with a name as well as a code.
    /// </param>
    /// <param name="CustomerVatNumber">
    /// The account's VAT number, or null. Not validated against VIES; see
    /// <see cref="EuB2bTaxRuleSet"/>.
    /// </param>
    /// <param name="IsTaxable">
    /// <c>Product.IsTaxable</c>. A product the store has marked exempt is exempt whoever buys
    /// it, which is why this is checked before anything about the customer.
    /// </param>
    public sealed record TaxContext(
        string SiteCountry,
        decimal StandardRatePct,
        string CustomerCountry,
        string CustomerVatNumber,
        bool IsTaxable);

    /// <summary>
    /// What to charge, and the sentence that has to appear on the document saying why.
    /// </summary>
    public sealed record TaxAssessment(string Treatment, decimal RatePct, string Legend)
    {
        /// <summary>The tax on a net amount, rounded the way every other money figure here is.</summary>
        /// <remarks>
        /// <c>MidpointRounding.AwayFromZero</c> to match <c>PriceResolver</c> and the T-SQL
        /// <c>ROUND</c> in <c>fnCatalog_VisibleProducts</c>. Two lines of one order must not
        /// round differently from each other or from the price they are taxing.
        /// </remarks>
        public decimal On(decimal netAmount) =>
            Math.Round(netAmount * RatePct / 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The tax rules one store trades under.
    /// </summary>
    /// <remarks>
    /// A rule set rather than a rate column because reverse charge is a decision tree, not a
    /// number: whether the customer is in the store's country, whether they are elsewhere in
    /// the EU, whether they produced a VAT number, and a different legend on the document for
    /// each answer. The rate is configuration and lives on <c>dbo.Site</c>; this is the logic
    /// that decides whether the rate applies at all.
    ///
    /// Selected by <c>Site.TaxRuleSet</c> through <see cref="TaxRuleSetProvider"/>, the same
    /// shape as <c>IRegistrationFieldSet</c> and <c>IOrderingMode</c>. **Nothing outside this
    /// namespace branches on <c>Site.TaxRuleSet</c>** — ask the provider.
    /// </remarks>
    public interface ITaxRuleSet
    {
        /// <summary>Matches <c>Site.TaxRuleSet</c>.</summary>
        string Key { get; }

        TaxAssessment Assess(TaxContext context);
    }
}
