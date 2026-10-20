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
                    query.PageSize,
                    Options = ProductOptionTable.From(query.Options.Select(option => (option.Name, option.Value)))
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
        /// Reads the three result sets spCatalog_GetFacets returns: categories, brands and, since
        /// T11, option values.
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
                query.Search,
                Options = ProductOptionTable.From(query.Options.Select(option => (option.Name, option.Value)))
            };

            var (categories, brands, options) = _sqlDataAccess
                .LoadThreeResultSets<CatalogFacetModel, CatalogFacetModel, CatalogOptionFacetModel, dynamic>(
                    "dbo.spCatalog_GetFacets", parameters, "SMDatabase");

            return new CatalogFacets { Categories = categories, Brands = brands, Options = options };
        }

        public CatalogItemModel GetBySku(int siteId, string sku, int? customerGroupId)
        {
            return _sqlDataAccess.LoadData<CatalogItemModel, dynamic>(
                "dbo.spCatalog_GetBySku",
                new { SiteId = siteId, Sku = sku, CustomerGroupId = customerGroupId },
                "SMDatabase").FirstOrDefault();
        }

        public CatalogFamily GetFamily(int siteId, int styleId, int? customerGroupId)
        {
            var (rows, options) = _sqlDataAccess
                .LoadTwoResultSets<CatalogItemModel, ProductOptionModel, dynamic>(
                    "dbo.spCatalog_GetFamily",
                    new { SiteId = siteId, ProductId = styleId, CustomerGroupId = customerGroupId },
                    "SMDatabase");

            return new CatalogFamily
            {
                Style = rows.FirstOrDefault(row => row.Id == styleId && row.ParentProductId is null),
                Variants = rows.Where(row => row.ParentProductId == styleId).ToList(),
                Options = options
            };
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
