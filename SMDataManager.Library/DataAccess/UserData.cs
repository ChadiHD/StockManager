using Dapper;
using Microsoft.Extensions.Configuration;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDataManager.Library.DataAccess
{
    public class UserData : IUserData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public UserData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }
        public List<UserModel> GetUserById(string UserId)
        {
            var output = _sqlDataAccess.LoadData<UserModel, dynamic>("dbo.spUserLookup", new { UserId }, "SMDatabase");

            return output;
        }

        public List<UserModel> GetAllUsers()
        {
            return _sqlDataAccess.LoadData<UserModel, dynamic>("dbo.spUser_GetAll", new { }, "SMDatabase");
        }

        public void CreateUser(UserModel user)
        {
            _sqlDataAccess.SaveData("dbo.spUser_Insert", new {user.UserId, user.FirstName, user.LastName, user.EmailAddress, user.AllSites}, "SMDatabase");
        }

        public bool CanActForSite(string userId, int siteId) =>
            _sqlDataAccess.LoadData<bool, dynamic>(
                "dbo.spUserSite_CanAct", new { UserId = userId, SiteId = siteId }, "SMDatabase").FirstOrDefault();

        public List<UserSiteModel> GetSiteGrants(string userId = null) =>
            _sqlDataAccess.LoadData<UserSiteModel, dynamic>(
                "dbo.spUserSite_Get", new { UserId = userId }, "SMDatabase");

        public void SetSiteAccess(string userId, bool allSites, IEnumerable<int> siteIds)
        {
            var table = new DataTable();
            table.Columns.Add("SiteId", typeof(int));

            // dbo.SiteIdList is keyed, so a list naming a store twice would fail the call.
            foreach (var siteId in (siteIds ?? Enumerable.Empty<int>()).Distinct())
            {
                table.Rows.Add(siteId);
            }

            _sqlDataAccess.SaveData("dbo.spUserSite_Set", new
            {
                UserId = userId,
                AllSites = allSites,
                SiteIds = table.AsTableValuedParameter("dbo.SiteIdList")
            }, "SMDatabase");
        }
    }
}
