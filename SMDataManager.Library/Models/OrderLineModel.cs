namespace SMDataManager.Library.Models
{
    public class OrderLineModel
    {
        public int Id { get; set; }
        public int PurchaseId { get; set; }
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal VAT { get; set; }

        /// <summary>The rate that produced the VAT beside it, snapshotted at acceptance.</summary>
        public decimal TaxRatePct { get; set; }
    }
}
