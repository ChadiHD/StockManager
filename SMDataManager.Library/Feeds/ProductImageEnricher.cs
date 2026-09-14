using Microsoft.Extensions.Logging;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SMDataManager.Library.Feeds
{
    // Walks products that have no image and asks Icecat for one, a batch at a time.
    //
    // Deliberately batched and out-of-band: a feed sync brings in thousands of products, and
    // doing a lookup per product inside the sync request would make it take minutes and time
    // out — the same mistake the bulk upsert removed.
    public class ProductImageEnricher : IProductImageEnricher
    {
        private readonly IProductData _productData;
        private readonly IIcecatClient _icecat;
        private readonly IBrandAliasResolver _brands;
        private readonly ILogger<ProductImageEnricher> _logger;

        public ProductImageEnricher(
            IProductData productData,
            IIcecatClient icecat,
            IBrandAliasResolver brands,
            ILogger<ProductImageEnricher> logger)
        {
            _productData = productData;
            _icecat = icecat;
            _brands = brands;
            _logger = logger;
        }

        public async Task<ImageEnrichmentResult> EnrichAsync(int take, CancellationToken cancellationToken = default)
        {
            var result = new ImageEnrichmentResult { Enabled = _icecat.IsConfigured };

            if (!_icecat.IsConfigured)
            {
                result.Message = "No Icecat account is configured, so image enrichment is switched off.";
                return result;
            }

            var candidates = _productData.GetImageCandidates(take);
            result.Considered = candidates.Count;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Feeds ship their own brand vocabulary — FlexIT's "HPINC" matches nothing
                    // at Icecat, and "UNIVERSAL" is not a manufacturer at all. Translate before
                    // querying, and clear the brand entirely when the value is not a brand so
                    // the client goes straight to its EAN fallback instead of spending a
                    // request to be told no.
                    var brand = _brands.Resolve(candidate.Distributor, candidate.Manufacturer);
                    candidate.Manufacturer = brand.Usable ? brand.Brand : null;

                    var imageUrl = await _icecat.FindImageUrlAsync(candidate, cancellationToken);

                    // Recorded either way: a null result still stamps the attempt so the same
                    // product is not retried on every pass.
                    _productData.SetImage(candidate.Id, imageUrl);

                    if (string.IsNullOrWhiteSpace(imageUrl)) result.NotFound++;
                    else result.Matched++;
                }
                // Only a real cancellation abandons the pass. An HttpClient timeout arrives as
                // TaskCanceledException with the token untouched, and rethrowing that would
                // stop the worker outright rather than costing one product its lookup.
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    result.Failed++;
                    _logger.LogWarning(exception,
                        "Icecat lookup failed for product {Sku} ({Manufacturer} {Mpn}).",
                        candidate.Sku, candidate.Manufacturer, candidate.ManufacturerPartNumber);
                }
            }

            result.Message =
                $"Considered {result.Considered}: {result.Matched} matched, {result.NotFound} with no match, {result.Failed} failed.";

            return result;
        }
    }
}
