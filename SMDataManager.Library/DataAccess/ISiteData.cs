using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface ISiteData
    {
        List<SiteModel> GetSites();
        SiteModel GetSiteByDomain(string domain);
        SiteModel GetSiteByKey(string siteKey);
    }
}
