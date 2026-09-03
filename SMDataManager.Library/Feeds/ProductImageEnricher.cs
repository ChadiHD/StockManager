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
        private readonly ILogger<ProductImageEnricher> _logger;

        public ProductImageEnricher(
            IProductData productData,
            IIcecatClient icecat,
            ILogger<ProductImageEnricher> logger)
        {
            _productData = productData;
            _icecat = icecat;
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
                    var imageUrl = await _icecat.FindImageUrlAsync(candidate, cancellationToken);

                    // Recorded either way: a null result still stamps the attempt so the same
                    // product is not retried on every pass.
                    _productData.SetImage(candidate.Id, imageUrl);

                    if (string.IsNullOrWhiteSpace(imageUrl)) result.NotFound++;
                    else result.Matched++;
                }
                catch (OperationCanceledException)
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
