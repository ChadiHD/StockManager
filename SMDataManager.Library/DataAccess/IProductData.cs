using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IProductData
    {
        ProductModel GetProductById(int productId);
        /// <summary>The desktop till's list: products in <paramref name="currencyCode"/>, or all when null.</summary>
        List<ProductModel> GetProducts(string currencyCode);

        // Admin catalog surface. Uses AdminProductModel so ProductModel (bound by the desktop
        // POS) keeps its original shape.

        /// <summary>Every product, with where it sits on <paramref name="siteId"/>.</summary>
        List<AdminProductModel> GetCatalog(int siteId);

        /// <summary>One store's choice about one product. Refused with a reason, never thrown.</summary>
        PlacementResult SetPlacement(
            int siteId, string sku, string visibility, int? siteCategoryId, bool featured, string badge);

        /// <summary>Shows, hides or resets a selection of products on one store, all or none.</summary>
        PlacementResult SetVisibility(
            int siteId, IEnumerable<string> skus, string visibility, int? siteCategoryId);

        /// <summary>A product <paramref name="siteId"/> may sell, by SKU (T9): unique within a feed, not across them.</summary>
        AdminProductModel GetProductBySku(int siteId, string sku);
        /// <summary>Own stock, priced in the acting store's currency, read back as that store sees it.</summary>
        AdminProductModel CreateProduct(AdminProductModel product, int siteId, string currencyCode);
        void UpdateProduct(AdminProductModel product);

        /// <summary>
        /// Applies an entire distributor feed in a single round trip, and delists products the
        /// feed no longer supplies. Keyed on the feed, not its name (T9).
        /// </summary>
        FeedUpsertResult BulkUpsertFromFeed(int feedId, string distributor, IEnumerable<DistributorFeedRecord> records);

        /// <summary>Products still missing an image that have something Icecat can be queried with.</summary>
        List<ProductImageCandidate> GetImageCandidates(int take);

        /// <summary>
        /// Records an image lookup. Pass null or empty to note that nothing was found, which
        /// still stamps the attempt so it is not retried immediately.
        /// </summary>
        void SetImage(int productId, string imageUrl);
    }
}
