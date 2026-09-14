using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// Trading accounts, scoped to a store.
    /// </summary>
    /// <remarks>
    /// Every method takes a siteId, and it is not optional anywhere. The underlying procedures
    /// treat it as a security predicate rather than a filter — an id belonging to another
    /// store reads as "not found" and writes affect nothing — so a caller has to state which
    /// store it is acting for before it can ask a question at all.
    ///
    /// It is the last parameter throughout, matching the procedures. Pass it from a resolved
    /// site rather than from a loose int: <c>GetAccountById(id, site.Id)</c> is hard to
    /// transpose by accident, <c>GetAccountById(a, b)</c> is not, and the compiler cannot tell
    /// two ints apart.
    /// </remarks>
    public interface IAccountData
    {
        List<AccountModel> GetAccounts(int siteId);
        AccountModel GetAccountById(int id, int siteId);
        AccountModel CreateAccount(AccountModel account, int siteId);
        void UpdateStatus(int id, string status, int siteId);
        void UpdateTerms(int id, int? customerGroupId, string paymentMethod, string paymentTerms, decimal creditLimit, int siteId);
    }
}
