using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface ISiteData
    {
        List<SiteModel> GetSites();
        SiteModel GetSiteByDomain(string domain);
        SiteModel GetSiteByKey(string siteKey);

        /// <summary>One store's whole configuration, active or not; null when there is none.</summary>
        SiteSettingsModel GetSettings(int siteId);

        /// <summary>Saves a store's settings. Returns why the database refused, or null.</summary>
        string UpdateSettings(SiteModel site);
    }
}
