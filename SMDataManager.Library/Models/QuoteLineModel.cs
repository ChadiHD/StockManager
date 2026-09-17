namespace SMDataManager.Library.Models
{
    public class QuoteLineModel
    {
        public int Id { get; set; }
        public int QuoteId { get; set; }
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public decimal ListPrice { get; set; }
        /// <summary>The discount that produced <see cref="NetPrice"/>, to two places.</summary>
        /// <remarks>
        /// Decimal because a price held up by the site's margin floor has a fractional
        /// effective discount — see spQuoteLine_Insert.
        /// </remarks>
        public decimal DiscountPct { get; set; }
        public decimal NetPrice { get; set; }
    }
}
