using System;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.Tax
{
    /// <summary>
    /// Why an order was taxed the way it was, snapshotted onto the order itself.
    /// </summary>
    /// <remarks>
    /// The treatment is stored rather than re-derived because it is the answer to a question
    /// somebody asks months later, and by then the rate may have changed, the rule set may
    /// have been corrected, and the customer may have supplied a VAT number they did not have
    /// at the time. T5 settled the same argument for price: what the customer was shown is
    /// what gets stored.
    /// </remarks>
    public static class TaxTreatment
    {
        /// <summary>The customer is in the store's own country. The standard rate applies.</summary>
        public const string DomesticStandard = "Domestic standard";

        /// <summary>
        /// Elsewhere in the EU, with a VAT number: the customer accounts for the tax.
        /// </summary>
        public const string IntraEuReverseCharge = "Intra-EU reverse charge";

        /// <summary>Outside the EU. Zero-rated as an export.</summary>
        public const string Export = "Export";

        /// <summary>The product is not taxable, whoever is buying it.</summary>
        public const string NotTaxable = "Not taxable";

        public static readonly IReadOnlyList<string> All =
            new[] { DomesticStandard, IntraEuReverseCharge, Export, NotTaxable };

        public static bool IsKnown(string treatment) =>
            All.Any(known => string.Equals(known, treatment, StringComparison.Ordinal));
    }
}
