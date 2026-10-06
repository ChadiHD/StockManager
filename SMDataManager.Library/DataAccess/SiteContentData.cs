using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class SiteContentData : ISiteContentData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public SiteContentData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        /// <summary>
        /// Returns null when the store has published nothing under this key. The storefront
        /// renders an empty state for that rather than substituting anything, because the
        /// alternative on a multi-store platform is showing another tenant's words.
        /// </summary>
        public SiteContentModel GetByKey(int siteId, string contentKey, string locale)
        {
            return _sqlDataAccess.LoadData<SiteContentModel, dynamic>(
                "dbo.spSiteContent_GetByKey",
                new { SiteId = siteId, ContentKey = contentKey, Locale = locale },
                "SMDatabase").FirstOrDefault();
        }

        public List<SiteContentModel> GetForSite(int siteId, string locale)
        {
            return _sqlDataAccess.LoadData<SiteContentModel, dynamic>(
                "dbo.spSiteContent_GetForSite", new { SiteId = siteId, Locale = locale }, "SMDatabase");
        }

        public SiteContentModel Save(int siteId, string contentKey, string locale, string title, string lede, string bodyHtml)
        {
            return _sqlDataAccess.LoadData<SiteContentModel, dynamic>(
                "dbo.spSiteContent_Save",
                new { SiteId = siteId, ContentKey = contentKey, Locale = locale, Title = title, Lede = lede, BodyHtml = bodyHtml },
                "SMDatabase").FirstOrDefault();
        }
    }
}
