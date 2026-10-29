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

        /// <summary>Why this order was taxed the way it was. NULL on a POS row.</summary>
        public string TaxTreatment { get; set; }

        /// <summary>The sentence printed on the document, snapshotted at acceptance.</summary>
        public string TaxLegend { get; set; }
        public decimal FinalPrice { get; set; }
        public string FromQuoteReference { get; set; }

        /// <summary>The customer's own purchase-order number, captured at acceptance.</summary>
        public string PoNumber { get; set; }
        public int Items { get; set; }

        /// <summary>The delivery charged (T13), on the store's basis; already in the totals.</summary>
        public string ShippingName { get; set; }
        public decimal ShippingPrice { get; set; }

        /// <summary>Given when a cart order is marked fulfilled (T13).</summary>
        public string TrackingNumber { get; set; }

        /// <summary>The account's address, which the hosted payment page starts with (T13). Customer reads only.</summary>
        public string AccountEmail { get; set; }

        /// <summary>
        /// The order's fiscal document (T14): a <see cref="FiscalDocumentStatus"/>, or null when it
        /// owes none. With its number, link and last error. Admin reads only.
        /// </summary>
        public string FiscalStatus { get; set; }
        public string FiscalNumber { get; set; }
        public string FiscalUrl { get; set; }
        public string FiscalError { get; set; }
    }
}
