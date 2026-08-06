using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IAccountData
    {
        List<AccountModel> GetAccounts();
        AccountModel GetAccountById(int id);
        AccountModel CreateAccount(AccountModel account);
        void UpdateStatus(int id, string status);
        void UpdateTerms(int id, int? customerGroupId, string paymentMethod, string paymentTerms, decimal creditLimit);
    }
}
