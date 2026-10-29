using System;
using System.Collections.Generic;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    /// <summary><c>dbo.FiscalDocument</c> (T14). Rows are written by the order procedures, never here.</summary>
    public interface IFiscalDocumentData
    {
        /// <summary>Takes up to <paramref name="batchSize"/> due documents under one claim.</summary>
        List<FiscalDocumentModel> Claim(Guid claimToken, int batchSize, int leaseMinutes);

        /// <summary>False when the claim no longer holds the row.</summary>
        bool RecordIssued(int id, Guid claimToken, string number, string url);

        /// <summary>
        /// Records a failed attempt. A null <paramref name="retryAfter"/> dead-letters it.
        /// False when the claim no longer holds the row.
        /// </summary>
        bool RecordFailure(int id, Guid claimToken, string error, TimeSpan? retryAfter);

        /// <summary>Queues a dead-lettered document again. False when it was not dead-lettered.</summary>
        bool Retry(int siteId, string reference);
    }
}
