using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class AccountDocumentData : IAccountDocumentData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public AccountDocumentData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<AccountDocumentModel> GetByAccount(int accountId, int siteId)
        {
            return _sqlDataAccess.LoadData<AccountDocumentModel, dynamic>(
                "dbo.spAccountDocument_GetByAccount",
                new { AccountId = accountId, SiteId = siteId }, "SMDatabase");
        }

        public AccountDocumentModel GetById(int id, int siteId)
        {
            return _sqlDataAccess.LoadData<AccountDocumentModel, dynamic>(
                "dbo.spAccountDocument_GetById", new { Id = id, SiteId = siteId },
                "SMDatabase").FirstOrDefault();
        }

        public AccountDocumentModel Insert(AccountDocumentModel document, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spAccountDocument_Insert", new
            {
                Id = 0,
                document.AccountId,
                document.Kind,
                document.StoredName,
                document.OriginalName,
                document.ContentType,
                document.SizeBytes,
                document.UploadedByContactId,
                SiteId = siteId
            }, "SMDatabase");

            // StoredName would identify this exactly and the list deliberately does not carry
            // it, so fall back to the newest row with the same original name. Two uploads of
            // one filename are indistinguishable here; taking the newest is right for the
            // caller that just made one.
            return GetByAccount(document.AccountId, siteId)
                .OrderByDescending(candidate => candidate.Id)
                .FirstOrDefault(candidate => candidate.OriginalName == document.OriginalName);
        }

        public void SetStatus(int id, string status, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spAccountDocument_SetStatus",
                new { Id = id, Status = status, SiteId = siteId }, "SMDatabase");
        }
    }
}
