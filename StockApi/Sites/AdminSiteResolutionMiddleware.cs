using Microsoft.Extensions.Caching.Memory;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;

namespace StockApi.Sites
{
    /// <summary>
    /// Resolves the store an admin request is acting for from its X-Site-Key header, and turns
    /// a controller's attempt to work without one into a 400 rather than a 500.
    /// </summary>
    /// <remarks>
    /// This never rejects a request on its own. Most of the API — tokens, identity, the
    /// desktop app's inventory and purchase endpoints — has nothing to do with storefronts and
    /// must keep working without the header, and middleware cannot tell which route needs a
    /// site. So resolution is best-effort here and the demand is made where it is actually
    /// known: reading <see cref="IAdminSiteContext.Site"/> throws, and that is caught below.
    /// </remarks>
    public sealed class AdminSiteResolutionMiddleware
    {
        public const string HeaderName = "X-Site-Key";

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);

        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AdminSiteResolutionMiddleware> _logger;

        public AdminSiteResolutionMiddleware(
            RequestDelegate next,
            IMemoryCache cache,
            ILogger<AdminSiteResolutionMiddleware> logger)
        {
            _next = next;
            _cache = cache;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, AdminSiteContext siteContext, ISiteData sites)
        {
            var site = Resolve(context, sites);

            if (site is not null)
            {
                siteContext.Resolve(site);
            }

            try
            {
                await _next(context);
            }
            catch (SiteNotResolvedException exception) when (!context.Response.HasStarted)
            {
                _logger.LogWarning(
                    "{Path} needs a store and the request named none.", context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new { error = exception.Message });
            }
        }

        private SiteModel Resolve(HttpContext context, ISiteData sites)
        {
            var key = context.Request.Headers[HeaderName].ToString();

            if (string.IsNullOrWhiteSpace(key))
            {
                // A database with exactly one store has only one possible answer, so requiring
                // the header there would be ceremony. The moment a second store exists the
                // guess stops being safe, and callers get the 400 instead — the same rule
                // dbo.fnSite_Resolve applies to writes.
                var active = Lookup("all", () => sites.GetSites().Where(s => s.IsActive).ToList());

                return active.Count == 1 ? active[0] : null;
            }

            // Cached because every admin request pays for this, and a site row changes about
            // as often as a deployment.
            var match = Lookup(key, () =>
            {
                var found = sites.GetSiteByKey(key);
                return found is null ? new List<SiteModel>() : new List<SiteModel> { found };
            });

            return match.FirstOrDefault(s => s.IsActive);
        }

        private List<SiteModel> Lookup(string key, Func<List<SiteModel>> load) =>
            _cache.GetOrCreate($"adminsite:{key}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
                return load();
            });
    }
}
