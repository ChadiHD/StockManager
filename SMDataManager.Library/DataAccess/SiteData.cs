using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class SiteData : ISiteData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public SiteData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<SiteModel> GetSites()
        {
            return _sqlDataAccess.LoadData<SiteModel, dynamic>(
                "dbo.spSite_GetAll", new { }, "SMDatabase");
        }

        /// <summary>
        /// Resolves a request host to a site. Returns null for an unknown or inactive domain —
        /// the caller decides what that means, which is a 404 in the storefront.
        /// </summary>
        public SiteModel GetSiteByDomain(string domain)
        {
            return _sqlDataAccess.LoadData<SiteModel, dynamic>(
                "dbo.spSite_GetByDomain", new { Domain = domain }, "SMDatabase").FirstOrDefault();
        }

        public SiteModel GetSiteByKey(string siteKey)
        {
            return _sqlDataAccess.LoadData<SiteModel, dynamic>(
                "dbo.spSite_GetByKey", new { SiteKey = siteKey }, "SMDatabase").FirstOrDefault();
        }
    }
}
