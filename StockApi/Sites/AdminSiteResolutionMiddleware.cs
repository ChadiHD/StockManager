using System.Security.Claims;
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

        public async Task InvokeAsync(
            HttpContext context, AdminSiteContext siteContext, ISiteData sites, IUserData users)
        {
            var site = Resolve(context, sites);

            if (site is not null && MayActFor(context, site, users))
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

        private SiteModel? Resolve(HttpContext context, ISiteData sites)
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

        /*
        Whether this caller may act for the store, from dbo.UserSite (T9). A store they may not
        act for is left unresolved, exactly like a key that names nothing, so the API's answer
        is the same 400 either way: "not yours" and "not here" are one answer, as they are for
        documents, and the message cannot be used to learn which stores exist.

        Not cached, unlike the site row: taking a store away from somebody has to apply to their
        next request. It is one primary-key read, paid only by a request that resolved a store.
        An anonymous caller acts for nothing.
        */
        private bool MayActFor(HttpContext context, SiteModel site, IUserData users)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrEmpty(userId) && users.CanActForSite(userId, site.Id))
            {
                return true;
            }

            // Only worth saying when the store was named: the single-store fallback resolves for
            // every till request too, and a till user has no stores by design.
            if (!string.IsNullOrWhiteSpace(context.Request.Headers[HeaderName].ToString()))
            {
                _logger.LogWarning(
                    "User {User} named store {SiteKey}, which they may not act for.", userId, site.SiteKey);
            }

            return false;
        }

        /// <summary>
        /// Drops the cached rows for a store, after its settings change (T9), so the API's next
        /// request reads what was just saved rather than what was there a minute ago.
        /// </summary>
        public static void Forget(IMemoryCache cache, string siteKey)
        {
            cache.Remove($"adminsite:{siteKey}");
            cache.Remove("adminsite:all");
        }

        private List<SiteModel> Lookup(string key, Func<List<SiteModel>> load) =>
            _cache.GetOrCreate($"adminsite:{key}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
                return load();
            });
    }
}
