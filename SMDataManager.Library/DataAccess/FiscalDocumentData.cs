using System;
using System.Collections.Generic;
using System.Linq;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public class FiscalDocumentData : IFiscalDocumentData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public FiscalDocumentData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<FiscalDocumentModel> Claim(Guid claimToken, int batchSize, int leaseMinutes)
        {
            return _sqlDataAccess.LoadData<FiscalDocumentModel, dynamic>(
                "dbo.spFiscalDocument_Claim",
                new { ClaimToken = claimToken, BatchSize = batchSize, LeaseMinutes = leaseMinutes },
                "SMDatabase");
        }

        public bool RecordIssued(int id, Guid claimToken, string number, string url)
        {
            return _sqlDataAccess.LoadData<int, dynamic>(
                "dbo.spFiscalDocument_RecordIssued",
                new { Id = id, ClaimToken = claimToken, Number = number, Url = url },
                "SMDatabase").FirstOrDefault() == 1;
        }

        public bool RecordFailure(int id, Guid claimToken, string error, TimeSpan? retryAfter)
        {
            // Truncated for EmailOutboxData's reason: a failure the column refuses leaves the
            // document stuck in Issuing until its lease runs out.
            var trimmed = error is { Length: > EmailOutboxData.ErrorLength } ? error[..EmailOutboxData.ErrorLength] : error;

            return _sqlDataAccess.LoadData<int, dynamic>(
                "dbo.spFiscalDocument_RecordFailure",
                new
                {
                    Id = id,
                    ClaimToken = claimToken,
                    Error = trimmed,
                    RetryAfterSeconds = retryAfter is { } wait ? (int?)Math.Ceiling(wait.TotalSeconds) : null
                },
                "SMDatabase").FirstOrDefault() == 1;
        }

        public bool Retry(int siteId, string reference)
        {
            return _sqlDataAccess.LoadData<int, dynamic>(
                "dbo.spFiscalDocument_Retry",
                new { SiteId = siteId, Reference = reference },
                "SMDatabase").FirstOrDefault() == 1;
        }
    }
}
