using System;
using System.Collections.Generic;

namespace SMDataManager.Library.Tax
{
    /// <summary>
    /// VAT for a store selling goods business-to-business from Great Britain (T9).
    /// </summary>
    /// <remarks>
    /// **This is an engine, not tax advice**, as <see cref="EuB2bTaxRuleSet"/> says of itself.
    /// The rules below are the plan's reading, and an accountant signs them off before the store
    /// takes an order (T9 plan, §7).
    ///
    /// Simpler than the EU's, because the UK left the single VAT area: there is no intra-EU
    /// reverse charge to qualify for, so a VAT number changes nothing here. A customer in the
    /// United Kingdom pays the standard rate — Northern Ireland included, which is UK VAT
    /// territory for a sale from Great Britain — and goods going anywhere else are an export,
    /// zero-rated, the EU and Ireland included.
    ///
    /// **Not built: the domestic reverse charge on mobile phones and computer chips**, which
    /// applies to supplies of £5,000 or more to a VAT-registered business and which an IT
    /// reseller can reach. It turns on what a line is, not on who the customer is, so it is a
    /// per-line decision this engine does not make; if the accountant says it applies, it is a
    /// phase of its own.
    /// </remarks>
    public sealed class UkB2bTaxRuleSet : ITaxRuleSet
    {
        public const string RuleSetKey = "uk-b2b";

        public string Key => RuleSetKey;

        /*
        The names a registration form or an admin may have typed for the United Kingdom. Account.
        Country is free text, so "GB" and "United Kingdom" both have to land in the same branch.
        Wrong in the safe direction charges VAT; wrong in the other zero-rates a domestic sale,
        which is the store's money. UK is not an ISO code but is what people type.
        */
        private static readonly HashSet<string> UnitedKingdom = new(StringComparer.OrdinalIgnoreCase)
        {
            "GB", "UK", "United Kingdom", "Great Britain", "England", "Scotland", "Wales",
            "Northern Ireland", "XI"
        };

        public TaxAssessment Assess(TaxContext context)
        {
            // A product the store marked exempt is exempt for everybody, as in every rule set.
            if (!context.IsTaxable)
            {
                return new TaxAssessment(TaxTreatment.NotTaxable, 0m,
                    "No VAT is chargeable on these goods.");
            }

            var customer = context.CustomerCountry?.Trim();

            // No country recorded is domestic, for the reason it is in the EU rule set: a blank
            // field must not be the cheapest way to avoid VAT.
            if (string.IsNullOrWhiteSpace(customer)
                || UnitedKingdom.Contains(customer)
                || string.Equals(customer, context.SiteCountry?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return new TaxAssessment(TaxTreatment.DomesticStandard, context.StandardRatePct,
                    $"VAT at {context.StandardRatePct:0.##}%.");
            }

            return new TaxAssessment(TaxTreatment.Export, 0m,
                "Zero-rated export. No UK VAT is chargeable.");
        }
    }
}
