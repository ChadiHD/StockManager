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
                Currency = string.IsNullOrWhiteSpace(account.Currency) ? "EUR" : account.Currency,
                account.CustomerGroupId,
                PaymentMethod = string.IsNullOrWhiteSpace(account.PaymentMethod) ? "Card" : account.PaymentMethod,
                PaymentTerms = string.IsNullOrWhiteSpace(account.PaymentTerms) ? "Prepaid" : account.PaymentTerms,
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

        public void UpdateTerms(int id, int? customerGroupId, string paymentMethod, string paymentTerms, decimal creditLimit, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spAccount_UpdateTerms", new
            {
                Id = id,
                CustomerGroupId = customerGroupId,
                PaymentMethod = paymentMethod,
                PaymentTerms = paymentTerms,
                CreditLimit = creditLimit,
                SiteId = siteId
            }, "SMDatabase");
        }
    }
}
