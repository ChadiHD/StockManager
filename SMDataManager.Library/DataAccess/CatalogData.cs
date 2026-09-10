using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class CatalogData : ICatalogData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public CatalogData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public CatalogPage Search(CatalogQuery query)
        {
            var rows = _sqlDataAccess.LoadData<CatalogItemModel, dynamic>(
                "dbo.spCatalog_Search",
                new
                {
                    query.SiteId,
                    query.CustomerGroupId,
                    query.CategorySlug,
                    query.Brand,
                    query.InStockOnly,
                    query.Search,
                    query.Sort,
                    query.Page,
                    query.PageSize
                },
                "SMDatabase");

            return new CatalogPage
            {
                Items = rows,
                // The window function repeats the total on every row, so an empty page means
                // zero matches rather than an unknown total.
                TotalCount = rows.Count == 0 ? 0 : rows[0].TotalCount,
                Page = query.Page,
                PageSize = query.PageSize
            };
        }

        /// <summary>
        /// Reads the two result sets spCatalog_GetFacets returns. This is the one place the
        /// codebase needs more than a single grid back from a procedure, which is why it uses
        /// the transaction helpers rather than LoadData.
        /// </summary>
        public CatalogFacets GetFacets(CatalogQuery query)
        {
            var parameters = new
            {
                query.SiteId,
                query.CustomerGroupId,
                query.CategorySlug,
                query.Brand,
                query.InStockOnly,
                query.Search
            };

            var (categories, brands) = _sqlDataAccess
                .LoadTwoResultSets<CatalogFacetModel, CatalogFacetModel, dynamic>(
                    "dbo.spCatalog_GetFacets", parameters, "SMDatabase");

            return new CatalogFacets { Categories = categories, Brands = brands };
        }

        public CatalogItemModel GetBySku(int siteId, string sku, int? customerGroupId)
        {
            return _sqlDataAccess.LoadData<CatalogItemModel, dynamic>(
                "dbo.spCatalog_GetBySku",
                new { SiteId = siteId, Sku = sku, CustomerGroupId = customerGroupId },
                "SMDatabase").FirstOrDefault();
        }

        public List<SiteCategoryModel> GetCategories(int siteId)
        {
            return _sqlDataAccess.LoadData<SiteCategoryModel, dynamic>(
                "dbo.spSiteCategory_GetBySite", new { SiteId = siteId }, "SMDatabase");
        }

        public List<UnmappedCategoryModel> GetUnmappedCategories(int siteId)
        {
            return _sqlDataAccess.LoadData<UnmappedCategoryModel, dynamic>(
                "dbo.spCategoryMapping_GetUnmapped", new { SiteId = siteId }, "SMDatabase");
        }
    }
}
