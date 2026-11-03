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
            // No endpoint means authorization had nothing to demand, so an anonymous caller got
            // this far — and every distinct header value it sent cost a query and a cache entry.
            // Nothing will read the store for a 404.
            if (context.GetEndpoint() is null)
            {
                await _next(context);
                return;
            }

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
                var active = Lookup(ListEntry, () => sites.GetSites().Where(s => s.IsActive).ToList());

                return active.Count == 1 ? active[0] : null;
            }

            // The column's width. Anything longer names no store, and is not worth a query or a
            // cache entry.
            if (key.Length > 50)
            {
                return null;
            }

            // Cached because every admin request pays for this, and a site row changes about
            // as often as a deployment. Inactive stores included (T9): a store is created
            // inactive and configured before it opens, and MayActFor decides who may see one.
            // Its own prefix, so a store keyed "all" — or a header saying so — cannot read or
            // overwrite the list above.
            return Lookup(KeyEntry(key), () => sites.GetSites().Where(s => s.SiteKey == key).ToList())
                .FirstOrDefault();
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

            // A store that is not open yet is being set up, which is an admin of every store's
            // job: they created it and they open it. Anybody else meets it as nothing.
            var allowed = !string.IsNullOrEmpty(userId) && (site.IsActive
                ? users.CanActForSite(userId, site.Id)
                : users.GetUserById(userId).FirstOrDefault()?.AllSites == true);

            if (allowed)
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
            cache.Remove(KeyEntry(siteKey));
            cache.Remove(ListEntry);
        }

        private const string ListEntry = "adminsite-list";

        private static string KeyEntry(string siteKey) => $"adminsite-key:{siteKey}";

        private List<SiteModel> Lookup(string cacheKey, Func<List<SiteModel>> load) =>
            _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
                return load();
            });
    }
}
