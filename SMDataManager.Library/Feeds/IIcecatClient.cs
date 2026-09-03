using SMDataManager.Library.Models;
using System.Threading;
using System.Threading.Tasks;

namespace SMDataManager.Library.Feeds
{
    // Resolves product content from Icecat. Implemented over Icecat's live JSON API in the API
    // project so this library takes no HTTP or credential dependency.
    public interface IIcecatClient
    {
        /// <summary>False when no Icecat account is configured, so callers can no-op cleanly.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Returns an image URL for the product, or null when Icecat has no match. Looks up by
        /// brand + part number first (present on every feed row), then falls back to GTIN/EAN.
        /// </summary>
        Task<string> FindImageUrlAsync(ProductImageCandidate candidate, CancellationToken cancellationToken = default);
    }

    public interface IProductImageEnricher
    {
        /// <summary>Enriches up to <paramref name="take"/> products that are missing an image.</summary>
        Task<ImageEnrichmentResult> EnrichAsync(int take, CancellationToken cancellationToken = default);
    }
}
