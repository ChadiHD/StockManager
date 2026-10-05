using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;
using StockApi.Feeds;
using StockApi.Sites;
using System.Data;

namespace StockApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    // Authenticated at the class level only. A controller-level Roles filter would be ANDed
    // with the action-level one, so "Staff" here plus "Admin" on the catalog actions would
    // demand BOTH roles and reject an administrator with 403.
    [Authorize]
    public class ProductController : ControllerBase
    {
        private readonly IProductData _productData;

        public ProductController(IProductData productData)
        {
            _productData = productData;
        }

        // Used by the desktop POS.
        [Authorize(Roles = "Staff")]
        [HttpGet]
        public List<ProductModel> Get()
        {
            var products = _productData.GetProducts();
            return products;
        }

        // ---- Admin catalog ------------------------------------------------------------
        // The class-level gate is Staff (the desktop POS); these are Admin-only and carry
        // their own attribute, which overrides it.

        /// <summary>Every product, with where it sits on the store the admin is acting for.</summary>
        /// <remarks>
        /// The site is injected per action, as for <see cref="SyncFeeds"/>: the POS's
        /// <see cref="Get"/> has no store and must not start needing one.
        /// </remarks>
        [Authorize(Roles = "Admin")]
        [HttpGet("Catalog")]
        public List<AdminProductModel> GetCatalog([FromServices] IAdminSiteContext site)
        {
            return _productData.GetCatalog(site.SiteId);
        }

        public record PlacementModel(string? Visibility, int? SiteCategoryId, bool Featured, string? Badge);

        /// <summary>
        /// One store's choice about one product: shown, hidden or left to the category mapping,
        /// where it is filed, and its featured flag and badge.
        /// </summary>
        /// <remarks>
        /// The procedure refuses a category from another store, and a Show with nowhere to file
        /// the product. Those come back as a 400 carrying the procedure's own sentence, because
        /// "the request was bad" is no help to an admin who needs to know which rule it broke.
        /// </remarks>
        [Authorize(Roles = "Admin")]
        [HttpPut("Catalog/{sku}/Placement")]
        public IActionResult SetPlacement(
            string sku, PlacementModel placement, [FromServices] IAdminSiteContext site)
        {
            if (!IsVisibility(placement.Visibility))
            {
                return BadRequest("Visibility is Show, Hide, or neither.");
            }

            if (_productData.GetProductBySku(sku) is null)
            {
                return NotFound();
            }

            var result = _productData.SetPlacement(
                site.SiteId, sku, placement.Visibility, placement.SiteCategoryId,
                placement.Featured, placement.Badge);

            return result.Saved ? NoContent() : BadRequest(result.Refusal);
        }

        public record BulkVisibilityModel(List<string> Skus, string? Visibility, int? SiteCategoryId);

        /// <summary>
        /// Shows, hides or hands back to the mapping a selection of products, all or none.
        /// Featured and badge are left as they were.
        /// </summary>
        [Authorize(Roles = "Admin")]
        [HttpPost("Catalog/Placement")]
        public ActionResult<int> SetVisibility(
            BulkVisibilityModel change, [FromServices] IAdminSiteContext site)
        {
            if (!IsVisibility(change.Visibility))
            {
                return BadRequest("Visibility is Show, Hide, or neither.");
            }

            // The table parameter is one round trip whatever its size, but a selection is
            // something a person made on a screen, and this is the ceiling of that.
            if (change.Skus is not { Count: > 0 and <= MaxSelection })
            {
                return BadRequest($"Choose between 1 and {MaxSelection} products.");
            }

            var result = _productData.SetVisibility(
                site.SiteId, change.Skus, change.Visibility, change.SiteCategoryId);

            return result.Saved ? result.Matched : BadRequest(result.Refusal);
        }

        private const int MaxSelection = 1000;

        // Refused here as well as in the procedure, because CK_SiteProduct_Visibility raising
        // from inside it would reach the portal as a 500.
        private static bool IsVisibility(string? visibility) =>
            visibility is null or "Show" or "Hide";

        [Authorize(Roles = "Admin")]
        [HttpGet("Catalog/{sku}")]
        public ActionResult<AdminProductModel> GetBySku(string sku)
        {
            var product = _productData.GetProductBySku(sku);

            return product is null ? NotFound() : product;
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("Catalog")]
        public ActionResult<AdminProductModel> Create(AdminProductModel product)
        {
            if (string.IsNullOrWhiteSpace(product.Sku) || string.IsNullOrWhiteSpace(product.ProductName))
            {
                return BadRequest("Sku and ProductName are required.");
            }

            if (_productData.GetProductBySku(product.Sku) is not null)
            {
                return Conflict($"A product with SKU '{product.Sku}' already exists.");
            }

            return _productData.CreateProduct(product);
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("Catalog/{sku}")]
        public IActionResult Update(string sku, AdminProductModel product)
        {
            var existing = _productData.GetProductBySku(sku);
            if (existing is null)
            {
                return NotFound();
            }

            product.Id = existing.Id;
            _productData.UpdateProduct(product);

            return NoContent();
        }

        // Pulls the configured distributor SFTP feeds and upserts what they contain.
        // Products added from your own warehouse use POST Catalog with Source = "Own" and are
        // never touched by a feed sync (it matches on Distributor + DistributorSku).
        // Convenience entry point for the catalogue page's "sync feeds" action. Feed management
        // itself lives on DistributorFeedController.
        // Manual trigger for image enrichment. The background service works through the backlog
        // on its own; this lets an operator kick a batch off immediately.
        [Authorize(Roles = "Admin")]
        [HttpPost("Catalog/EnrichImages")]
        public async Task<ActionResult<ImageEnrichmentResult>> EnrichImages(
            [FromServices] IProductImageEnricher enricher,
            [FromQuery] int take = 50,
            CancellationToken cancellationToken = default)
        {
            return await enricher.EnrichAsync(Math.Clamp(take, 1, 500), cancellationToken);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("Catalog/Sync")]
        public async Task<ActionResult<List<DistributorFeedResult>>> SyncFeeds(
            [FromServices] IDistributorFeedSyncService feedSync,
            [FromServices] IAdminSiteContext site)
        {
            // One store's feeds, not every store's. Injected per action rather than through
            // the constructor because the rest of this controller serves the desktop POS,
            // which has no site and must not start needing one.
            var results = await feedSync.SyncAllAsync(site.SiteId, FeedSyncTrigger.Operator);

            // Surface a total failure rather than reporting a clean sync. A feed that was
            // already running does not count as one — see FeedSyncOutcome.
            if (FeedSyncOutcome.IsTotalFailure(results))
            {
                return StatusCode(StatusCodes.Status502BadGateway, results);
            }

            return results;
        }
    }
}
