using System;

namespace SMDataManager.Library.Tax
{
    /// <summary>
    /// One rate on every taxable sale, wherever the buyer is (T10).
    /// </summary>
    /// <remarks>
    /// **This is an engine, not tax advice**, as <see cref="EuB2bTaxRuleSet"/> says of itself. An
    /// accountant signs it off for each store that uses it, before that store takes an order.
    ///
    /// This is how a shop selling to consumers is taxed at home. Brazil, Ireland, the UK and
    /// Australia work this way, and so does an EU shop under the distance-selling threshold, which
    /// charges its own country's rate to a buyer anywhere in the EU. The treatment is therefore
    /// <see cref="TaxTreatment.DomesticStandard"/> for every buyer: it is the store's domestic tax
    /// that is charged.
    ///
    /// It reads no VAT number, because a consumer has none, and no customer country, because a
    /// rate that turns on the destination belongs to a destination rule set. That one needs the
    /// delivery address an order does not carry yet (T10 plan, §5).
    /// </remarks>
    public sealed class FlatTaxRuleSet : ITaxRuleSet
    {
        public const string RuleSetKey = "flat";

        public string Key => RuleSetKey;

        public TaxAssessment Assess(TaxContext context) =>
            context.IsTaxable
                ? new TaxAssessment(TaxTreatment.DomesticStandard, context.StandardRatePct,
                    FormattableString.Invariant($"Tax at {context.StandardRatePct:0.##}%."))
                : new TaxAssessment(TaxTreatment.NotTaxable, 0m, "No tax is chargeable on these goods.");
    }
}
