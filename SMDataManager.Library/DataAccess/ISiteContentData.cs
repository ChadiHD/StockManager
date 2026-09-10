using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public interface ISiteContentData
    {
        SiteContentModel GetByKey(int siteId, string contentKey, string locale);
    }
}
