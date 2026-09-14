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

        /// <summary>
        /// Company identifiers collected at registration. Which of them are demanded is the
        /// site's registration field set's decision, so either may be absent.
        /// </summary>
        public string VatNumber { get; set; }
        public string RegistrationNumber { get; set; }

        /// <summary>
        /// Who decided the application and when. Set by both spAccount_Approve and
        /// spAccount_Reject — this records the decision, not specifically the approval.
        /// </summary>
        public DateTime? ApprovedUtc { get; set; }
        public string ApprovedBy { get; set; }

        /// <summary>What the rejection email quotes. Cleared when an account is approved.</summary>
        public string RejectionReason { get; set; }
    }
}
