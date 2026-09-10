using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;

namespace SMStore.Sites;

/// <summary>
/// Maps a request host to a <see cref="SiteModel"/>, with a short cache so the lookup does not
/// cost a database round trip on every page.
/// </summary>
public sealed class SiteResolver
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly SiteOptions _options;
    private readonly ILogger<SiteResolver> _logger;

    public SiteResolver(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IOptions<SiteOptions> options,
        ILogger<SiteResolver> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public SiteModel? Resolve(string host)
    {
        // ForceSiteKey wins so a developer can serve a real store from localhost. Program.cs
        // refuses to start with it set outside development.
        var forced = _options.ForceSiteKey;
        if (!string.IsNullOrWhiteSpace(forced))
        {
            return Lookup($"key:{forced}", data => data.GetSiteByKey(forced));
        }

        // Request.Host.Host already excludes the port, so localhost:7260 and localhost:443 are
        // the same site. Domains are case-insensitive; the cache key must be too.
        var domain = host.ToLowerInvariant();

        return Lookup($"domain:{domain}", data => data.GetSiteByDomain(domain));
    }

    private SiteModel? Lookup(string cacheKey, Func<ISiteData, SiteModel?> query)
    {
        if (_cache.TryGetValue<SiteModel>(cacheKey, out var cached))
        {
            return cached;
        }

        // Resolution runs from middleware, which may sit outside a DI scope that owns the
        // scoped ISiteData, so take an explicit scope for the query.
        using var scope = _scopeFactory.CreateScope();
        var site = query(scope.ServiceProvider.GetRequiredService<ISiteData>());

        if (site is null)
        {
            // Cached as a miss too, briefly: an unknown host is usually a scan or a stale DNS
            // record, and those repeat. Shorter than a hit so a newly added site appears soon.
            _cache.Set<SiteModel?>(cacheKey, null, TimeSpan.FromSeconds(30));
            _logger.LogWarning("No active site matched {CacheKey}.", cacheKey);
            return null;
        }

        _cache.Set(cacheKey, site, _options.CacheDuration);
        return site;
    }
}
