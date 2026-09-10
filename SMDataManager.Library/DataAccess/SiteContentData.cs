using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
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
    }
}
