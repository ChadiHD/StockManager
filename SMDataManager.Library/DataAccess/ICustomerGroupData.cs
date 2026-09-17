using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// Pricing groups, scoped to a store. See <see cref="IAccountData"/> for why siteId is
    /// mandatory and last on every method.
    /// </summary>
    /// <remarks>
    /// A slug is unique per site rather than globally, so here the site is not only a security
    /// predicate — without it a lookup by slug is genuinely ambiguous once a second store
    /// names a group the same thing.
    /// </remarks>
    public interface ICustomerGroupData
    {
        List<CustomerGroupModel> GetGroups(int siteId);
        CustomerGroupModel GetGroupBySlug(string slug, int siteId);
        CustomerGroupModel CreateGroup(CustomerGroupModel group, int siteId);
        void UpdateGroup(string slug, int discount, string terms, string note, int siteId);
    }
}
