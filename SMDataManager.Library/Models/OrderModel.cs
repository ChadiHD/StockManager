using System;

namespace SMDataManager.Library.Models
{
    // A sales order: a dbo.Purchase row that carries a Reference. POS sales written by the
    // desktop app have a NULL Reference and are not surfaced as orders.
    public class OrderModel
    {
        public int Id { get; set; }
        public string Reference { get; set; }
        public int? AccountId { get; set; }
        public string AccountName { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public DateTime PurchaseDate { get; set; }
        public decimal SubTotal { get; set; }
        public decimal VAT { get; set; }
        public decimal FinalPrice { get; set; }
        public string FromQuoteReference { get; set; }

        /// <summary>The customer's own purchase-order number, captured at acceptance.</summary>
        public string PoNumber { get; set; }
        public int Items { get; set; }
    }
}
