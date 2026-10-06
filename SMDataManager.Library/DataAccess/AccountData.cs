using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class AccountData : IAccountData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public AccountData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<AccountModel> GetAccounts(int siteId)
        {
            return _sqlDataAccess.LoadData<AccountModel, dynamic>(
                "dbo.spAccount_GetAll", new { SiteId = siteId }, "SMDatabase");
        }

        public AccountModel GetAccountById(int id, int siteId)
        {
            return _sqlDataAccess.LoadData<AccountModel, dynamic>(
                "dbo.spAccount_GetById", new { Id = id, SiteId = siteId }, "SMDatabase").FirstOrDefault();
        }

        public AccountModel CreateAccount(AccountModel account, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spAccount_Insert", new
            {
                Id = 0,
                account.Company,
                account.ContactName,
                account.Email,
                account.Country,
                account.CustomerGroupId,
                PaymentMethod = string.IsNullOrWhiteSpace(account.PaymentMethod) ? "Card" : account.PaymentMethod,
                PaymentTerms = Terms(account.PaymentTerms),
                PaymentTermsDays = PaymentTerms.DaysFor(Terms(account.PaymentTerms)),
                account.CreditLimit,
                Status = string.IsNullOrWhiteSpace(account.Status) ? "Pending" : account.Status,
                SiteId = siteId
            }, "SMDatabase");

            // The reference is generated inside the procedure, so re-read to return the row
            // exactly as stored.
            return GetAccounts(siteId).FirstOrDefault(x => x.Company == account.Company);
        }

        public void UpdateStatus(int id, string status, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spAccount_UpdateStatus",
                new { Id = id, Status = status, SiteId = siteId }, "SMDatabase");
        }

        public bool Approve(int id, string approvedBy, int? customerGroupId, int siteId)
        {
            // The procedure returns its row count rather than an output parameter, because
            // Dapper cannot write one back through an anonymous object.
            return _sqlDataAccess.LoadData<int, dynamic>("dbo.spAccount_Approve", new
            {
                Id = id,
                ApprovedBy = approvedBy,
                CustomerGroupId = customerGroupId,
                SiteId = siteId
            }, "SMDatabase").FirstOrDefault() > 0;
        }

        public bool Reject(int id, string approvedBy, string reason, int siteId)
        {
            return _sqlDataAccess.LoadData<int, dynamic>("dbo.spAccount_Reject", new
            {
                Id = id,
                ApprovedBy = approvedBy,
                Reason = reason,
                SiteId = siteId
            }, "SMDatabase").FirstOrDefault() > 0;
        }

        public void UpdateTerms(int id, int? customerGroupId, string paymentMethod, string paymentTerms, decimal creditLimit, int siteId)
        {
            // Days are derived here rather than taken as a parameter, so the label and the
            // number cannot arrive disagreeing. CK_Account_Terms would refuse the pair, and a
            // constraint violation from inside a procedure reaches the caller as a 500 — which
            // is why AccountController refuses an unknown label with a 400 first, exactly as
            // QuoteController refuses an unknown status.
            string terms = Terms(paymentTerms);

            _sqlDataAccess.SaveData("dbo.spAccount_UpdateTerms", new
            {
                Id = id,
                CustomerGroupId = customerGroupId,
                PaymentMethod = paymentMethod,
                PaymentTerms = terms,
                PaymentTermsDays = PaymentTerms.DaysFor(terms),
                CreditLimit = creditLimit,
                SiteId = siteId
            }, "SMDatabase");
        }

        private static string Terms(string paymentTerms) =>
            string.IsNullOrWhiteSpace(paymentTerms) ? PaymentTerms.Prepaid : paymentTerms;
    }
}
