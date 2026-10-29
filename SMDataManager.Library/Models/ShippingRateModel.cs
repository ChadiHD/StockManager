namespace SMDataManager.Library.Models
{
    /// <summary>One of a store's delivery rates, as <c>dbo.ShippingRate</c> holds it (T13).</summary>
    public class ShippingRateModel
    {
        public int Id { get; set; }
        public string Name { get; set; }

        /// <summary>Comma-separated ISO 3166-1 alpha-2 codes, or null for every country.</summary>
        public string Countries { get; set; }

        /// <summary>On the store's basis: with tax in on an inclusive store.</summary>
        public decimal Price { get; set; }

        /// <summary>Waived when the goods come to this or more, on the same basis. Null never waives.</summary>
        public decimal? FreeOver { get; set; }

        public bool IsActive { get; set; }
    }
}
