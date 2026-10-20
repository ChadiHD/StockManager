using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface ICatalogData
    {
        CatalogPage Search(CatalogQuery query);

        CatalogFacets GetFacets(CatalogQuery query);

        CatalogItemModel GetBySku(int siteId, string sku, int? customerGroupId);

        /// <summary>A style and its variants for this viewer (T11).</summary>
        CatalogFamily GetFamily(int siteId, int styleId, int? customerGroupId);

        /// <summary>A product's photographs in order, as stored names (T11). Public, so not scoped.</summary>
        List<string> GetImages(int productId);

        List<SiteCategoryModel> GetCategories(int siteId);

        /// <summary>Feed category values with no mapping for this store, and what they are holding back.</summary>
        List<UnmappedCategoryModel> GetUnmappedCategories(int siteId);
    }
}
