using System;

namespace SMDataManager.Library.Models
{
    // One row of /admin/reports. Distinct from PurchaseReportModel, which reports POS sales
    // by staff member rather than sales orders by account.
    public class SalesReportModel
    {
        public DateTime Date { get; set; }
        public string Account { get; set; }
        public string Ref { get; set; }
        public string Currency { get; set; }
        public decimal Net { get; set; }
        public decimal Vat { get; set; }
        public decimal Total { get; set; }
    }
}
