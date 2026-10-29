using System.Collections.Generic;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// The values a store's keyed settings may take: one list, read by the settings screen to
    /// offer them and by the API to refuse anything else (T9).
    /// </summary>
    /// <remarks>
    /// Each key names an implementation — an <c>IOrderingMode</c> or <c>IRegistrationFieldSet</c>
    /// in SMStore, an <c>ITaxRuleSet</c> here — and the provider that picks one throws on a key
    /// nothing implements. Until T9 that throw was the only validation, so a typo in a row
    /// written by hand was a 500 on the store's next request. The API cannot see SMStore's
    /// types, hence a list; <c>SiteSettingKeysTests</c> in SMStore.Tests fails if it and the
    /// implementations disagree in either direction.
    /// </remarks>
    public static class SiteSettingKeys
    {
        public static readonly IReadOnlyList<string> OrderModes = new[] { "Rfq", "DirectCheckout" };

        public static readonly IReadOnlyList<string> RegistrationFieldSets = new[] { "eu-b2b", "uk-b2b", "b2c" };

        public static readonly IReadOnlyList<string> TaxRuleSets = new[] { "eu-b2b", "uk-b2b", "flat" };

        /// <summary>Queues no fiscal documents: every store before T14, and the column's default.</summary>
        public const string NoFiscalIssuer = "none";

        /// <summary>
        /// <see cref="NoFiscalIssuer"/>, and each <c>IFiscalDocumentIssuer</c> in StockApi (T14).
        /// <c>FiscalIssuerKeysTests</c> in StockApi.Tests holds the two together.
        /// </summary>
        public static readonly IReadOnlyList<string> FiscalIssuers = new[] { NoFiscalIssuer, "logging" };

        /// <summary>Also enforced by <c>CK_Site_PriceDisplay</c>.</summary>
        public static readonly IReadOnlyList<string> PriceDisplays = new[] { "Public", "Authenticated" };
    }
}
