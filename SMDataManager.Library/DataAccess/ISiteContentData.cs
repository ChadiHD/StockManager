using System.Collections.Generic;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public interface ISiteContentData
    {
        SiteContentModel GetByKey(int siteId, string contentKey, string locale);

        /// <summary>For each key the store has written, the row its storefront renders (T9).</summary>
        List<SiteContentModel> GetForSite(int siteId, string locale);

        /// <summary>
        /// Writes the row the storefront renders for <paramref name="contentKey"/> and returns it
        /// as stored. <paramref name="bodyHtml"/> must already be sanitized.
        /// </summary>
        SiteContentModel Save(int siteId, string contentKey, string locale, string title, string lede, string bodyHtml);
    }
}
