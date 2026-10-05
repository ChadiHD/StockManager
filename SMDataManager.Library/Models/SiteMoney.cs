using System.Globalization;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// An amount as a store writes it: its locale's format, its own currency's symbol.
    /// </summary>
    /// <remarks>
    /// Moved here from <c>CatalogPresenter</c> when mail started quoting prices. An order
    /// confirmation rendered by <c>StockApi</c> and the order page rendered by the storefront
    /// must write the same total the same way, and two copies of the currency rule is how a
    /// customer receives "EUR 500.00" for a page that says "€500.00".
    /// </remarks>
    public static class SiteMoney
    {
        public static string Format(SiteModel site, decimal amount) =>
            amount.ToString("C", Culture(site));

        private static CultureInfo Culture(SiteModel site)
        {
            CultureInfo culture;

            try
            {
                culture = CultureInfo.GetCultureInfo(site.Locale ?? "en-IE");
            }
            catch (CultureNotFoundException)
            {
                // A misconfigured locale must not take the catalog down; fall back and let the
                // currency override below still get the symbol right.
                culture = CultureInfo.InvariantCulture;
            }

            var code = site.CurrencyCode;

            if (string.IsNullOrWhiteSpace(code))
            {
                return culture;
            }

            // The site's currency wins over whatever the locale implies: a store can trade in a
            // currency that is not its locale's default, and showing the wrong symbol on a price
            // is worse than showing an unfamiliar format.
            var withCurrency = (CultureInfo)culture.Clone();
            withCurrency.NumberFormat.CurrencySymbol = CurrencySymbol(code);

            return withCurrency;
        }

        private static string CurrencySymbol(string code) => code.ToUpperInvariant() switch
        {
            "EUR" => "€",
            "GBP" => "£",
            "USD" => "$",
            _ => code + " "
        };
    }
}
