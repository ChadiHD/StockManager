using SMDataManager.Library.Models;

namespace StockApi.Sites
{
    /// <summary>
    /// The store the current request is acting for.
    /// </summary>
    /// <remarks>
    /// The storefront resolves its site from the request host, because a visitor arrives at
    /// one store's domain. The admin API cannot: it is one host serving an operator who may
    /// run several stores, so the site is something the caller states rather than something
    /// the request reveals. <see cref="AdminSiteResolutionMiddleware"/> reads it from the
    /// X-Site-Key header.
    ///
    /// The header is trusted once the key names a real, active site that the caller may act
    /// for — every store for a user with <c>AllSites</c>, otherwise those in <c>dbo.UserSite</c>
    /// (T9). A store they may not act for leaves this unresolved, the same as an unknown key.
    /// </remarks>
    public interface IAdminSiteContext
    {
        /// <summary>
        /// True when the request named a store that exists, is active, and the caller may act for.
        /// </summary>
        bool IsResolved { get; }

        /// <summary>
        /// The resolved store. Throws <see cref="SiteNotResolvedException"/> when the request
        /// named none — deliberately, so a controller cannot read a site-scoped row without
        /// having been told which store is asking. Failing loudly is the point: the
        /// alternative is a default that quietly serves one tenant's data to another.
        /// </summary>
        SiteModel Site { get; }

        /// <summary>Shorthand for <see cref="Site"/>.Id, with the same failure behaviour.</summary>
        int SiteId { get; }
    }

    public sealed class AdminSiteContext : IAdminSiteContext
    {
        private SiteModel? _site;

        public bool IsResolved => _site is not null;

        public SiteModel Site =>
            _site ?? throw new SiteNotResolvedException();

        public int SiteId => Site.Id;

        internal void Resolve(SiteModel site) => _site = site;
    }
}
