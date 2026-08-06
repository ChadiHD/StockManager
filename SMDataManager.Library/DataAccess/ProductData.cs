using Dapper;
using Microsoft.Extensions.Configuration;
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
        public List<ProductModel> GetProducts()
        {
            var output = _sqlDataAccess.LoadData<ProductModel, dynamic>("dbo.spProduct_GetAll", new { }, "SMDatabase");

            return output;
        }

        public ProductModel GetProductById(int productId)
        {
            var output = _sqlDataAccess.LoadData<ProductModel, dynamic>("dbo.spProduct_GetById", new { Id = productId }, "SMDatabase").FirstOrDefault();

            return output;
        }

        public List<AdminProductModel> GetCatalog()
        {
            return _sqlDataAccess.LoadData<AdminProductModel, dynamic>(
                "dbo.spProduct_GetAll", new { }, "SMDatabase");
        }

        public AdminProductModel GetProductBySku(string sku)
        {
            return _sqlDataAccess.LoadData<AdminProductModel, dynamic>(
                "dbo.spProduct_GetBySku", new { Sku = sku }, "SMDatabase").FirstOrDefault();
        }

        public AdminProductModel CreateProduct(AdminProductModel product)
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
                product.ProductImage
            }, "SMDatabase");

            return GetProductBySku(product.Sku);
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

        public int SyncDistributorFeeds()
        {
            var output = _sqlDataAccess.LoadData<int, dynamic>(
                "dbo.spProduct_SyncFeeds", new { }, "SMDatabase");

            return output.FirstOrDefault();
        }

        public FeedUpsertResult BulkUpsertFromFeed(string distributor, IEnumerable<DistributorFeedRecord> records)
        {
            var table = BuildFeedTable(records);

            var output = _sqlDataAccess.LoadData<FeedUpsertResult, dynamic>(
                "dbo.spProduct_BulkUpsertFromFeed",
                new
                {
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
                    record.Quantity);
            }

            return table;
        }

        private static string Truncate(string value, int maxLength) =>
            string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
