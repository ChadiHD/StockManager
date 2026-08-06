using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IProductData
    {
        ProductModel GetProductById(int productId);
        List<ProductModel> GetProducts();

        // Admin catalog surface. Uses AdminProductModel so ProductModel (bound by the desktop
        // POS) keeps its original shape.
        List<AdminProductModel> GetCatalog();
        AdminProductModel GetProductBySku(string sku);
        AdminProductModel CreateProduct(AdminProductModel product);
        void UpdateProduct(AdminProductModel product);
        int SyncDistributorFeeds();

        /// <summary>
        /// Applies an entire distributor feed in a single round trip, and delists products the
        /// distributor no longer supplies.
        /// </summary>
        FeedUpsertResult BulkUpsertFromFeed(string distributor, IEnumerable<DistributorFeedRecord> records);
    }
}
