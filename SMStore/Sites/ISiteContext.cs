using SMDataManager.Library.Models;

namespace SMStore.Sites;

/// <summary>
/// The store being served by the current request. One SMStore deployment serves every site,
/// so nothing downstream may read a country, currency, tax rule or brand from configuration or
/// a constant — it comes from here.
/// </summary>
public interface ISiteContext
{
    /// <summary>
    /// The resolved site. Throws if resolution has not run or found nothing, because rendering
    /// a storefront without knowing which store it is would silently show another tenant's
    /// defaults. <see cref="SiteResolutionMiddleware"/> short-circuits unresolved hosts before
    /// any component runs, so component code can treat this as always present.
    /// </summary>
    SiteModel Site { get; }

    bool IsResolved { get; }
}
