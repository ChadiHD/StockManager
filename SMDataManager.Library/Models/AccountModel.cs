using System;

namespace SMDataManager.Library.Models
{
    public class AccountModel
    {
        public int Id { get; set; }
        public string Reference { get; set; }
        public string Company { get; set; }
        public string ContactName { get; set; }
        public string Email { get; set; }
        public string Country { get; set; }
        public string Currency { get; set; }
        public int? CustomerGroupId { get; set; }
        public string GroupName { get; set; }
        public string PaymentMethod { get; set; }
        public string PaymentTerms { get; set; }
        public decimal CreditLimit { get; set; }
        public string Status { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
