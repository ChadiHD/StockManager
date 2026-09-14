using System;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// Metadata for a file a customer uploaded to support their application. The bytes live
    /// in an IDocumentStore, outside the web root, reachable only through an endpoint that
    /// re-checks who is asking.
    /// </summary>
    public class AccountDocumentModel
    {
        public int Id { get; set; }
        public int AccountId { get; set; }

        /// <summary>"VatCertificate" | "ChamberOfCommerce" | "Other".</summary>
        public string Kind { get; set; }

        /// <summary>
        /// The key into the document store, and the means to fetch the file.
        /// </summary>
        /// <remarks>
        /// Null on anything that came from a list: only spAccountDocument_GetById projects
        /// it, because only the download endpoint needs it and it fetches one row at a time
        /// after checking the caller. Never put this in a response.
        /// </remarks>
        public string StoredName { get; set; }

        /// <summary>
        /// What the customer called the file. Display only, and as untrusted as it was on the
        /// way in — encode it on the way out.
        /// </summary>
        public string OriginalName { get; set; }

        /// <summary>As sniffed from the leading bytes, not as the client declared it.</summary>
        public string ContentType { get; set; }

        public long SizeBytes { get; set; }
        public int? UploadedByContactId { get; set; }
        public DateTime UploadedUtc { get; set; }

        /// <summary>"Pending" | "Accepted" | "Rejected", reviewed per document.</summary>
        public string Status { get; set; }
    }
}
