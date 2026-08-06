using System;

namespace SMDataManager.Library.Models
{
    public class QuoteModel
    {
        public int Id { get; set; }
        public string Reference { get; set; }
        public int AccountId { get; set; }
        public string AccountName { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ExpiresDate { get; set; }
        public int Lines { get; set; }
        public decimal Value { get; set; }
    }
}
