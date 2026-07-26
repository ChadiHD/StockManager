using System;

namespace SMDesktopUI.Library.Models
{
    public class PurchaseReportModel
    {
        public DateTime PurchaseDate { get; set; }
        public decimal SubTotal { get; set; }
        public decimal VAT { get; set; }
        public decimal FinalPrice { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmailAddress { get; set; }
    }
}
