using System;

namespace SMDataManager.Library.Models
{
    /// <summary>One row of <c>dbo.FiscalDocument</c>, as a claim returns it (T14).</summary>
    public class FiscalDocumentModel
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int PurchaseId { get; set; }

        /// <summary>The order's reference, which the dispatcher reads it by.</summary>
        public string Reference { get; set; }

        /// <summary>The issuer key the store had when the sale was made.</summary>
        public string Issuer { get; set; }

        /// <summary>Including the attempt this claim is for.</summary>
        public int Attempts { get; set; }
        public Guid ClaimToken { get; set; }
    }

    /// <summary>The four states a fiscal document can be in, and the only four the table accepts.</summary>
    public static class FiscalDocumentStatus
    {
        public const string Pending = "Pending";

        /// <summary>Claimed by a dispatcher, under a lease that expires.</summary>
        public const string Issuing = "Issuing";

        public const string Issued = "Issued";

        /// <summary>Refused, or failed every retry. The operator has been told, and it can be retried.</summary>
        public const string DeadLettered = "DeadLettered";
    }
}
