using System;
using System.Collections.Generic;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public interface IEmailOutboxData
    {
        void Enqueue(int siteId, string toAddress, string toName, string templateKey,
            string payloadJson, bool payloadProtected);

        /// <summary>Takes up to <paramref name="batchSize"/> due messages under one claim.</summary>
        List<EmailOutboxModel> Claim(Guid claimToken, int batchSize, int leaseMinutes);

        /// <summary>False when the claim no longer holds the row.</summary>
        bool RecordSent(int id, Guid claimToken);

        /// <summary>
        /// Records a failed attempt. A null <paramref name="retryAfter"/> dead-letters it.
        /// False when the claim no longer holds the row.
        /// </summary>
        bool RecordFailure(int id, Guid claimToken, string error, TimeSpan? retryAfter);
    }
}
