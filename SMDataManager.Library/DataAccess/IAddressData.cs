using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// Billing and delivery addresses, gated through their account for the site. See
    /// <see cref="IAccountData"/> for why siteId is mandatory and last on every method.
    /// </summary>
    public interface IAddressData
    {
        List<AddressModel> GetByAccount(int accountId, int siteId);
        AddressModel Insert(AddressModel address, int siteId);
        void Update(AddressModel address, int siteId);

        /// <summary>False when the address is not this store's, or was already gone.</summary>
        bool Delete(int id, int siteId);
    }
}
