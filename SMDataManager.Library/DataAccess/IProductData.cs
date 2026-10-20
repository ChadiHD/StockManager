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
        /// <summary>Own stock, priced in the acting store's currency and on its tax basis, read back as that store sees it.</summary>
        AdminProductModel CreateProduct(AdminProductModel product, SiteModel site);
        void UpdateProduct(AdminProductModel product);

        /// <summary>
        /// A size or colour of <paramref name="styleId"/> (T11), its options in the order given. Refused
        /// with a reason, never thrown, like a placement.
        /// </summary>
        VariantResult AddVariant(int siteId, int styleId, string sku, int quantityInStock,
            IEnumerable<(string Name, string Value)> options);

        /// <summary>Removes a variant nothing holds; otherwise the reason it cannot be.</summary>
        string RemoveVariant(int siteId, int variantId);

        /// <summary>A style's variants, for the admin's product page (T11).</summary>
        List<VariantModel> GetVariants(int siteId, int styleId);

        /// <summary>Records a photograph whose bytes are stored; the first becomes the product's image (T11).</summary>
        void AddImage(int productId, string storedName, string contentType);

        /// <summary>Forgets one, and says whether it was this product's.</summary>
        bool RemoveImage(int productId, string storedName);

        /// <summary>A product's photographs, in order: stored names.</summary>
        List<string> GetImages(int productId);

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
