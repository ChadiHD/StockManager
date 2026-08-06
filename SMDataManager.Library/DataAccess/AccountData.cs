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

        public List<AccountModel> GetAccounts()
        {
            return _sqlDataAccess.LoadData<AccountModel, dynamic>(
                "dbo.spAccount_GetAll", new { }, "SMDatabase");
        }

        public AccountModel GetAccountById(int id)
        {
            return _sqlDataAccess.LoadData<AccountModel, dynamic>(
                "dbo.spAccount_GetById", new { Id = id }, "SMDatabase").FirstOrDefault();
        }

        public AccountModel CreateAccount(AccountModel account)
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
                Status = string.IsNullOrWhiteSpace(account.Status) ? "Pending" : account.Status
            }, "SMDatabase");

            // The reference is generated inside the procedure, so re-read to return the row
            // exactly as stored.
            return GetAccounts().FirstOrDefault(x => x.Company == account.Company);
        }

        public void UpdateStatus(int id, string status)
        {
            _sqlDataAccess.SaveData("dbo.spAccount_UpdateStatus",
                new { Id = id, Status = status }, "SMDatabase");
        }

        public void UpdateTerms(int id, int? customerGroupId, string paymentMethod, string paymentTerms, decimal creditLimit)
        {
            _sqlDataAccess.SaveData("dbo.spAccount_UpdateTerms", new
            {
                Id = id,
                CustomerGroupId = customerGroupId,
                PaymentMethod = paymentMethod,
                PaymentTerms = paymentTerms,
                CreditLimit = creditLimit
            }, "SMDatabase");
        }
    }
}
