using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface ICatalogData
    {
        CatalogPage Search(CatalogQuery query);

        CatalogFacets GetFacets(CatalogQuery query);

        CatalogItemModel GetBySku(int siteId, string sku, int? customerGroupId);

        List<SiteCategoryModel> GetCategories(int siteId);

        /// <summary>Feed category values with no mapping for this store, and what they are holding back.</summary>
        List<UnmappedCategoryModel> GetUnmappedCategories(int siteId);
    }
}
