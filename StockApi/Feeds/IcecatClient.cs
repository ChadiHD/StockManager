using System.Net;
using System.Text.Json;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;

namespace StockApi.Feeds
{
    // Icecat live JSON API. Looks products up by brand + part number first, since the FlexIT
    // feed carries those on every row, and falls back to GTIN/EAN which only ~12% of rows have.
    //
    // The feed's "IceCatID" column is a Yes/empty availability flag, not an identifier, so it is
    // never used as a lookup key — only as a hint for ordering candidates.
    public class IcecatClient : IIcecatClient
    {
        private const string BaseAddress = "https://live.icecat.biz/api";

        private readonly HttpClient _client;
        private readonly ILogger<IcecatClient> _logger;
        private readonly string? _userName;
        private readonly string? _apiKey;
        private readonly string _language;

        public IcecatClient(HttpClient client, IConfiguration config, ILogger<IcecatClient> logger)
        {
            _client = client;
            _logger = logger;
            _userName = config["Icecat:UserName"];
            // Optional: Full Icecat accounts authenticate with a key as well as a username.
            _apiKey = config["Icecat:ApiKey"];
            _language = config["Icecat:Language"] ?? "en";
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_userName);

        public async Task<string?> FindImageUrlAsync(
            ProductImageCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return null;

            // Brand + MPN first: 100% coverage on the feed, and a more precise match than GTIN.
            if (!string.IsNullOrWhiteSpace(candidate.Manufacturer)
                && !string.IsNullOrWhiteSpace(candidate.ManufacturerPartNumber))
            {
                var byBrand = await QueryAsync(
                    $"Brand={Uri.EscapeDataString(candidate.Manufacturer)}" +
                    $"&ProductCode={Uri.EscapeDataString(candidate.ManufacturerPartNumber)}",
                    cancellationToken);

                if (byBrand is not null) return byBrand;
            }

            if (!string.IsNullOrWhiteSpace(candidate.Ean))
            {
                return await QueryAsync($"GTIN={Uri.EscapeDataString(candidate.Ean.Trim())}", cancellationToken);
            }

            return null;
        }

        private async Task<string?> QueryAsync(string keyQuery, CancellationToken cancellationToken)
        {
            var url = $"{BaseAddress}?UserName={Uri.EscapeDataString(_userName!)}" +
                      $"&Language={Uri.EscapeDataString(_language)}&{keyQuery}&Content=Image";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Add("api_token", _apiKey);
            }

            using var response = await _client.SendAsync(request, cancellationToken);

            // A product Icecat does not carry is an expected outcome, not a failure.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Icecat returned {Status} for {Query}.", (int)response.StatusCode, keyQuery);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            try
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                return ExtractImageUrl(document.RootElement);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Could not parse the Icecat response for {Query}.", keyQuery);
                return null;
            }
        }

        // Icecat nests images under data.Image (single) and data.Gallery (list), and the field
        // names vary by plan, so probe the known shapes rather than binding a rigid DTO.
        private static string? ExtractImageUrl(JsonElement root)
        {
            if (!root.TryGetProperty("data", out var data)) return null;

            if (data.TryGetProperty("Image", out var image))
            {
                var direct = FirstUrl(image, "HighPic", "Pic500x500", "LowPic", "ThumbPic");
                if (direct is not null) return direct;
            }

            if (data.TryGetProperty("Gallery", out var gallery) && gallery.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in gallery.EnumerateArray())
                {
                    var fromGallery = FirstUrl(entry, "Pic", "Pic500x500", "LowPic", "ThumbPic");
                    if (fromGallery is not null) return fromGallery;
                }
            }

            return null;
        }

        private static string? FirstUrl(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;

            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    var url = value.GetString();
                    if (!string.IsNullOrWhiteSpace(url)) return url;
                }
            }

            return null;
        }
    }
}
