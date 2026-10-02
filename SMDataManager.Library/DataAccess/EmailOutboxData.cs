using System;
using System.Collections.Generic;
using System.Linq;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public class EmailOutboxData : IEmailOutboxData
    {
        /// <summary>The width of <c>EmailOutbox.LastError</c>.</summary>
        public const int ErrorLength = 1000;

        private readonly ISqlDataAccess _sqlDataAccess;

        public EmailOutboxData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public void Enqueue(int siteId, string toAddress, string toName, string templateKey,
            string payloadJson, bool payloadProtected)
        {
            _sqlDataAccess.SaveData("dbo.spEmailOutbox_Enqueue", new
            {
                SiteId = siteId,
                ToAddress = toAddress,
                ToName = toName,
                TemplateKey = templateKey,
                PayloadJson = payloadJson,
                PayloadProtected = payloadProtected
            }, "SMDatabase");
        }

        public List<EmailOutboxModel> Claim(Guid claimToken, int batchSize, int leaseMinutes)
        {
            return _sqlDataAccess.LoadData<EmailOutboxModel, dynamic>(
                "dbo.spEmailOutbox_Claim",
                new { ClaimToken = claimToken, BatchSize = batchSize, LeaseMinutes = leaseMinutes },
                "SMDatabase");
        }

        public bool RecordSent(int id, Guid claimToken)
        {
            return _sqlDataAccess.LoadData<int, dynamic>(
                "dbo.spEmailOutbox_RecordSent",
                new { Id = id, ClaimToken = claimToken },
                "SMDatabase").FirstOrDefault() == 1;
        }

        public bool RecordFailure(int id, Guid claimToken, string error, TimeSpan? retryAfter)
        {
            // Truncated here because the column would otherwise refuse the row, and a failure
            // that cannot be recorded is a message stuck in Sending until its lease runs out.
            var trimmed = error is { Length: > ErrorLength } ? error[..ErrorLength] : error;

            return _sqlDataAccess.LoadData<int, dynamic>(
                "dbo.spEmailOutbox_RecordFailure",
                new
                {
                    Id = id,
                    ClaimToken = claimToken,
                    Error = trimmed,
                    RetryAfterSeconds = retryAfter is { } wait ? (int?)Math.Ceiling(wait.TotalSeconds) : null
                },
                "SMDatabase").FirstOrDefault() == 1;
        }
    }
}
