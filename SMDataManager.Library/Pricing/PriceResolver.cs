using System;

namespace SMDataManager.Library.Pricing
{
    public class PriceResolver : IPriceResolver
    {
        public ResolvedPrice Resolve(decimal listPrice, decimal? cost, decimal groupDiscountPct, decimal minMarginPct)
        {
            if (listPrice < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(listPrice), "A list price cannot be negative.");
            }

            decimal discount = Clamp(groupDiscountPct, 0m, 100m);

            decimal net = Round(listPrice * (1m - (discount / 100m)));

            bool floorApplied = false;

            // The floor exists so no group's discount sells stock at a loss. It only applies
            // where the buy price is actually known; a feed row without a cost gets the
            // group's rate as configured rather than a floor invented from nothing.
            if (cost.HasValue && cost.Value > 0m && minMarginPct > 0m)
            {
                decimal floor = Round(cost.Value * (1m + (minMarginPct / 100m)));

                // A floor above list means the product is mispriced — cost plus the required
                // margin exceeds what it is listed at. Raising the customer above list would
                // be worse than honouring list, so it is capped and the operator finds it
                // through the margin report rather than through a complaint.
                if (floor > listPrice)
                {
                    floor = listPrice;
                }

                if (net < floor)
                {
                    net = floor;
                    floorApplied = true;
                }
            }

            return new ResolvedPrice
            {
                ListPrice = listPrice,
                NetPrice = net,
                EffectiveDiscountPct = listPrice == 0m
                    ? 0m
                    : Round(100m * (listPrice - net) / listPrice),
                FloorApplied = floorApplied
            };
        }

        // Away from zero, matching how prices are quoted on paper; the framework default
        // (to even) would make two lines of the same product round differently.
        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static decimal Clamp(decimal value, decimal min, decimal max) =>
            value < min ? min : value > max ? max : value;
    }
}
