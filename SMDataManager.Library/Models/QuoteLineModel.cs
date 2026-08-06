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
        public int DiscountPct { get; set; }
        public decimal NetPrice { get; set; }
    }
}
