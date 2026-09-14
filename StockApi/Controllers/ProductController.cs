using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;
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

        [Authorize(Roles = "Admin")]
        [HttpGet("Catalog")]
        public List<AdminProductModel> GetCatalog()
        {
            return _productData.GetCatalog();
        }

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
            var results = await feedSync.SyncAllAsync(site.SiteId);

            // Surface a total failure rather than reporting a clean sync.
            if (results.Count > 0 && results.All(result => !result.Succeeded))
            {
                return StatusCode(StatusCodes.Status502BadGateway, results);
            }

            return results;
        }
    }
}
