using System;

namespace SMDataManager.Library.Models
{
    /// <summary>One row of <c>dbo.EmailOutbox</c>, as a claim returns it.</summary>
    public class EmailOutboxModel
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string ToAddress { get; set; }
        public string ToName { get; set; }
        public string TemplateKey { get; set; }

        /// <summary>
        /// JSON, or its ciphertext when <see cref="PayloadProtected"/> is set. Never log it.
        /// </summary>
        public string PayloadJson { get; set; }
        public bool PayloadProtected { get; set; }

        public string Status { get; set; }

        /// <summary>Including the attempt this claim is for.</summary>
        public int Attempts { get; set; }
        public Guid ClaimToken { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    /// <summary>The four states a queued message can be in, and the only four the table accepts.</summary>
    public static class EmailOutboxStatus
    {
        public const string Pending = "Pending";

        /// <summary>Claimed by a dispatcher, under a lease that expires.</summary>
        public const string Sending = "Sending";

        public const string Sent = "Sent";

        /// <summary>Given up on after every retry; the operator has been told.</summary>
        public const string DeadLettered = "DeadLettered";
    }
}
