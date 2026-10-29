using System.Collections.Generic;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>A store's delivery rates (T13), scoped by site like every store's own rows.</summary>
    public interface IShippingRateData
    {
        /// <summary>Active and inactive: the portal lists both, the storefront takes the active.</summary>
        List<ShippingRateModel> GetBySite(int siteId);

        /// <summary>Adds a rate when <c>Id</c> is zero, or changes one of this store's. False when there is no such rate here.</summary>
        bool Save(ShippingRateModel rate, int siteId);
    }
}
