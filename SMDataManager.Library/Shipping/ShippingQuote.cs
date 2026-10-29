using System;
using System.Collections.Generic;
using System.Linq;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.Shipping
{
    /// <summary>What delivery costs, and to where (T13).</summary>
    /// <remarks>
    /// One definition, used by the checkout page to show the charge and by the checkout to make
    /// it, so the two cannot disagree. Pure, so its tests need nothing.
    /// </remarks>
    public static class ShippingQuote
    {
        /// <summary>
        /// The cheapest active rate covering <paramref name="country"/>, with its price for goods
        /// worth <paramref name="goods"/>, or null when no rate covers it.
        /// </summary>
        /// <remarks>
        /// The cheapest, not the customer's choice: a flat-rate store's rates differ by where they
        /// go, and choosing between two for one country is a carrier feature this does not build.
        /// Compared after the waiver, so a dearer rate that is free over the threshold wins.
        /// </remarks>
        public static ShippingCharge Choose(IEnumerable<ShippingRateModel> rates, string country, decimal goods)
        {
            var code = country?.Trim().ToUpperInvariant();

            return rates
                .Where(rate => rate.IsActive && Covers(rate, code))
                .Select(rate => new ShippingCharge(rate.Name,
                    rate.FreeOver is { } threshold && goods >= threshold ? 0m : rate.Price))
                .OrderBy(charge => charge.Price)
                .FirstOrDefault();
        }

        public static bool Covers(ShippingRateModel rate, string country) =>
            string.IsNullOrWhiteSpace(rate.Countries)
            || Codes(rate.Countries).Contains(country, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A list as typed into the portal, as the column stores it: upper-case, comma-separated,
        /// each once, or null for every country.
        /// </summary>
        public static string Normalise(string countries)
        {
            var codes = Codes(countries).Select(code => code.ToUpperInvariant()).Distinct().ToList();

            return codes.Count == 0 ? null : string.Join(",", codes);
        }

        public static IEnumerable<string> Codes(string countries) =>
            (countries ?? string.Empty)
                .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(code => code.Trim());
    }

    /// <summary>The rate charged and what it costs, on the store's basis.</summary>
    public sealed record ShippingCharge(string Name, decimal Price);
}
