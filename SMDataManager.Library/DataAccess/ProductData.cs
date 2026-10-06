using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDataManager.Library.DataAccess
{
    public class ProductData : IProductData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public ProductData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }
        public List<ProductModel> GetProducts(string currencyCode)
        {
            var output = _sqlDataAccess.LoadData<ProductModel, dynamic>("dbo.spProduct_GetAll", new { CurrencyCode = currencyCode }, "SMDatabase");

            return output;
        }

        public ProductModel GetProductById(int productId)
        {
            var output = _sqlDataAccess.LoadData<ProductModel, dynamic>("dbo.spProduct_GetById", new { Id = productId }, "SMDatabase").FirstOrDefault();

            return output;
        }

        public List<AdminProductModel> GetCatalog(int siteId)
        {
            return _sqlDataAccess.LoadData<AdminProductModel, dynamic>(
                "dbo.spProduct_GetCatalogForSite", new { SiteId = siteId }, "SMDatabase");
        }

        /// <summary>
        /// The THROW numbers spSiteProduct_Set and spSiteProduct_SetVisibility refuse with:
        /// unknown SKU, bad visibility, foreign category, nowhere to file a shown product.
        /// </summary>
        private static bool IsPlacementRefusal(SqlException ex) => ex.Number is >= 50070 and <= 50073;

        public PlacementResult SetPlacement(
            int siteId, string sku, string visibility, int? siteCategoryId, bool featured, string badge)
        {
            try
            {
                _sqlDataAccess.SaveData("dbo.spSiteProduct_Set", new
                {
                    SiteId = siteId,
                    Sku = sku,
                    Visibility = visibility,
                    SiteCategoryId = siteCategoryId,
                    Featured = featured,
                    Badge = badge
                }, "SMDatabase");

                return new PlacementResult(1, null);
            }
            catch (SqlException ex) when (IsPlacementRefusal(ex))
            {
                return new PlacementResult(0, ex.Message);
            }
        }

        public PlacementResult SetVisibility(
            int siteId, IEnumerable<string> skus, string visibility, int? siteCategoryId)
        {
            var table = new DataTable();
            table.Columns.Add("Sku", typeof(string));

            // dbo.SkuList is keyed, so a selection naming a product twice would fail the batch.
            foreach (var sku in skus.Where(sku => !string.IsNullOrWhiteSpace(sku))
                         .Select(sku => sku.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                table.Rows.Add(sku);
            }

            try
            {
                var matched = _sqlDataAccess.LoadData<int, dynamic>("dbo.spSiteProduct_SetVisibility", new
                {
                    SiteId = siteId,
                    Skus = table.AsTableValuedParameter("dbo.SkuList"),
                    Visibility = visibility,
                    SiteCategoryId = siteCategoryId
                }, "SMDatabase").FirstOrDefault();

                return new PlacementResult(matched, null);
            }
            catch (SqlException ex) when (IsPlacementRefusal(ex))
            {
                return new PlacementResult(0, ex.Message);
            }
        }

        public AdminProductModel GetProductBySku(int siteId, string sku)
        {
            return _sqlDataAccess.LoadData<AdminProductModel, dynamic>(
                "dbo.spProduct_GetBySku", new { SiteId = siteId, Sku = sku }, "SMDatabase").FirstOrDefault();
        }

        public AdminProductModel CreateProduct(AdminProductModel product, int siteId, string currencyCode)
        {
            _sqlDataAccess.SaveData("dbo.spProduct_Insert", new
            {
                Id = 0,
                product.Sku,
                product.ProductName,
                Description = product.Description ?? string.Empty,
                product.Category,
                product.Source,
                product.Distributor,
                product.DistributorSku,
                product.Cost,
                product.RetailPrice,
                product.QuantityInStock,
                product.IsTaxable,
                product.ProductImage,
                CurrencyCode = currencyCode
            }, "SMDatabase");

            return GetProductBySku(siteId, product.Sku);
        }

        public void UpdateProduct(AdminProductModel product)
        {
            _sqlDataAccess.SaveData("dbo.spProduct_Update", new
            {
                product.Id,
                product.ProductName,
                Description = product.Description ?? string.Empty,
                product.Category,
                product.Source,
                product.Distributor,
                product.DistributorSku,
                product.Cost,
                product.RetailPrice,
                product.QuantityInStock,
                product.IsTaxable,
                product.ProductImage
            }, "SMDatabase");
        }

        public List<ProductImageCandidate> GetImageCandidates(int take)
        {
            return _sqlDataAccess.LoadData<ProductImageCandidate, dynamic>(
                "dbo.spProduct_GetImageCandidates", new { Take = take }, "SMDatabase");
        }

        public void SetImage(int productId, string imageUrl)
        {
            _sqlDataAccess.SaveData("dbo.spProduct_SetImage",
                new { Id = productId, ProductImage = imageUrl }, "SMDatabase");
        }

        public FeedUpsertResult BulkUpsertFromFeed(int feedId, string distributor, IEnumerable<DistributorFeedRecord> records)
        {
            var table = BuildFeedTable(records);

            var output = _sqlDataAccess.LoadData<FeedUpsertResult, dynamic>(
                "dbo.spProduct_BulkUpsertFromFeed",
                new
                {
                    FeedId = feedId,
                    Distributor = distributor,
                    Items = table.AsTableValuedParameter("dbo.DistributorFeedItem")
                },
                "SMDatabase");

            return output.FirstOrDefault() ?? new FeedUpsertResult();
        }

        // Column order must match dbo.DistributorFeedItem — table-valued parameters bind by
        // ordinal, not by name.
        private static DataTable BuildFeedTable(IEnumerable<DistributorFeedRecord> records)
        {
            var table = new DataTable();
            table.Columns.Add("DistributorSku", typeof(string));
            table.Columns.Add("Sku", typeof(string));
            table.Columns.Add("ProductName", typeof(string));
            table.Columns.Add("Description", typeof(string));
            table.Columns.Add("Category", typeof(string));
            table.Columns.Add("Cost", typeof(decimal));
            table.Columns.Add("Srp", typeof(decimal));
            table.Columns.Add("QuantityInStock", typeof(int));
            table.Columns.Add("Manufacturer", typeof(string));
            table.Columns.Add("ManufacturerPartNumber", typeof(string));
            table.Columns.Add("Ean", typeof(string));
            table.Columns.Add("IcecatAvailable", typeof(bool));

            // The table type is keyed on DistributorSku, so a feed that repeats a SKU would
            // otherwise fail the whole import. Last occurrence wins.
            var deduplicated = records
                .Where(record => !string.IsNullOrWhiteSpace(record.DistributorSku))
                .GroupBy(record => record.DistributorSku, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last());

            foreach (var record in deduplicated)
            {
                table.Rows.Add(
                    record.DistributorSku,
                    record.DistributorSku,
                    Truncate(record.Name, 100) ?? record.DistributorSku,
                    (object)record.Description ?? DBNull.Value,
                    (object)Truncate(record.Category, 50) ?? DBNull.Value,
                    record.Cost.HasValue ? record.Cost.Value : (object)DBNull.Value,
                    record.Srp.HasValue ? record.Srp.Value : (object)DBNull.Value,
                    record.Quantity,
                    (object)Truncate(record.Manufacturer, 100) ?? DBNull.Value,
                    (object)Truncate(record.Mpn, 100) ?? DBNull.Value,
                    (object)Truncate(record.Ean, 20) ?? DBNull.Value,
                    record.IcecatAvailable.HasValue ? record.IcecatAvailable.Value : (object)DBNull.Value);
            }

            return table;
        }

        private static string Truncate(string value, int maxLength) =>
            string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
