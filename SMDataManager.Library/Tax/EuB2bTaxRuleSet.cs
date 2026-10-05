using System;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.Tax
{
    /// <summary>
    /// VAT for a store selling business-to-business from inside the EU.
    /// </summary>
    /// <remarks>
    /// **This is an engine, not tax advice.** The design doc records that the reverse-charge
    /// treatment must be confirmed with an accountant before go-live, and that has not
    /// happened. What this guarantees is that whatever rules a store configures are applied
    /// the same way every time and recorded on the order, so a correction is auditable rather
    /// than archaeological.
    ///
    /// A VAT number is recorded and believed, not validated against VIES. That is a network
    /// call to a service with no availability guarantee, sitting on the path that raises an
    /// order, and designing its failure mode — refuse the order? charge the standard rate and
    /// refund later? queue it? — is a phase of its own. Believing a number given in good
    /// faith is what a supplier did before VIES had an API and is what the rules expect.
    /// </remarks>
    public sealed class EuB2bTaxRuleSet : ITaxRuleSet
    {
        public const string RuleSetKey = "eu-b2b";

        public string Key => RuleSetKey;

        /*
        EU membership, as codes and as the English names a registration form collects.

        Account.Country is free text — T3's registration field sets collect whatever the form
        asked for — so "IE" and "Ireland" both have to land in the same branch. Getting this
        wrong in the safe direction charges the standard rate; getting it wrong in the other
        direction zero-rates a domestic sale, which is the store's money.

        Membership changes rarely and is not per-tenant, so it is a constant rather than a
        table. A store outside the EU needs a different rule set, not a longer list.
        */
        private static readonly HashSet<string> EuMembers = new(StringComparer.OrdinalIgnoreCase)
        {
            "AT", "Austria", "BE", "Belgium", "BG", "Bulgaria", "HR", "Croatia",
            "CY", "Cyprus", "CZ", "Czechia", "Czech Republic", "DK", "Denmark",
            "EE", "Estonia", "FI", "Finland", "FR", "France", "DE", "Germany",
            "GR", "Greece", "HU", "Hungary", "IE", "Ireland", "IT", "Italy",
            "LV", "Latvia", "LT", "Lithuania", "LU", "Luxembourg", "MT", "Malta",
            "NL", "Netherlands", "PL", "Poland", "PT", "Portugal", "RO", "Romania",
            "SK", "Slovakia", "SI", "Slovenia", "ES", "Spain", "SE", "Sweden"
        };

        /// <summary>
        /// Country names that mean the same place, so a store configured "IE" and a customer
        /// who typed "Ireland" are not treated as cross-border.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> Codes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Austria"] = "AT", ["Belgium"] = "BE", ["Bulgaria"] = "BG",
                ["Croatia"] = "HR", ["Cyprus"] = "CY", ["Czechia"] = "CZ",
                ["Czech Republic"] = "CZ", ["Denmark"] = "DK", ["Estonia"] = "EE",
                ["Finland"] = "FI", ["France"] = "FR", ["Germany"] = "DE",
                ["Greece"] = "GR", ["Hungary"] = "HU", ["Ireland"] = "IE",
                ["Italy"] = "IT", ["Latvia"] = "LV", ["Lithuania"] = "LT",
                ["Luxembourg"] = "LU", ["Malta"] = "MT", ["Netherlands"] = "NL",
                ["Poland"] = "PL", ["Portugal"] = "PT", ["Romania"] = "RO",
                ["Slovakia"] = "SK", ["Slovenia"] = "SI", ["Spain"] = "ES",
                ["Sweden"] = "SE"
            };

        public TaxAssessment Assess(TaxContext context)
        {
            // Before anything about the customer: a product the store marked exempt is exempt
            // for everybody, and Product.IsTaxable is the flag the desktop till already
            // honours. One product must not be taxable at the counter and exempt on the web.
            if (!context.IsTaxable)
            {
                return new TaxAssessment(TaxTreatment.NotTaxable, 0m,
                    "No VAT is chargeable on these goods.");
            }

            string site = Normalise(context.SiteCountry);
            string customer = Normalise(context.CustomerCountry);

            // An account with no country recorded is treated as domestic. The alternative is
            // zero-rating on the strength of a blank field, which would make an incomplete
            // registration the cheapest way to avoid VAT.
            if (string.IsNullOrWhiteSpace(customer) || customer == site)
            {
                return Standard(context);
            }

            if (!EuMembers.Contains(customer))
            {
                return new TaxAssessment(TaxTreatment.Export, 0m,
                    "Zero-rated export. No VAT is chargeable.");
            }

            // Elsewhere in the EU, and the reverse charge turns on whether they gave a number.
            // Without one they are not evidenced as a business, and a consumer in another
            // member state pays the seller's rate.
            if (string.IsNullOrWhiteSpace(context.CustomerVatNumber))
            {
                return Standard(context);
            }

            return new TaxAssessment(TaxTreatment.IntraEuReverseCharge, 0m,
                "VAT reverse charge: the customer accounts for VAT under Article 196 of "
                + "Council Directive 2006/112/EC.");
        }

        private static TaxAssessment Standard(TaxContext context) =>
            new(TaxTreatment.DomesticStandard, context.StandardRatePct,
                $"VAT at {context.StandardRatePct:0.##}%.");

        private static string Normalise(string country)
        {
            if (string.IsNullOrWhiteSpace(country))
            {
                return string.Empty;
            }

            string trimmed = country.Trim();

            return Codes.TryGetValue(trimmed, out var code) ? code : trimmed.ToUpperInvariant();
        }
    }
}
