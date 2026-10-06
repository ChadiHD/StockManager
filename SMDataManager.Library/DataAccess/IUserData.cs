using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IUserData
    {
        void CreateUser(UserModel user);
        List<UserModel> GetUserById(string Id);
        List<UserModel> GetAllUsers();

        /// <summary>Whether the user may act for the store in the admin portal.</summary>
        bool CanActForSite(string userId, int siteId);

        /// <summary>The stores staff have been given: one user's, or everybody's when null.</summary>
        List<UserSiteModel> GetSiteGrants(string userId = null);

        /// <summary>Every store, or exactly <paramref name="siteIds"/>; replaces what was there.</summary>
        void SetSiteAccess(string userId, bool allSites, IEnumerable<int> siteIds);
    }
}
