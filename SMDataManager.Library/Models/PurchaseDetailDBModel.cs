namespace SMDataManager.Library.Models
{
    public class PurchaseDetailDBModel
    {
        public int PurchaseId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal VAT { get; set; }
    }
}
