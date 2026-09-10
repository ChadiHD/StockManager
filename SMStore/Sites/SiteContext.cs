using SMDataManager.Library.Models;

namespace SMStore.Sites;

/// <summary>
/// Scoped holder written once by <see cref="SiteResolutionMiddleware"/> at the start of the
/// request and read everywhere after. Scoped rather than singleton because the site varies per
/// request; for an interactive Server circuit the scope is the circuit, which cannot change
/// host mid-connection.
/// </summary>
public sealed class SiteContext : ISiteContext
{
    private SiteModel? _site;

    public SiteModel Site =>
        _site ?? throw new InvalidOperationException(
            "No site has been resolved for this request. SiteResolutionMiddleware must run " +
            "before anything that reads site configuration.");

    public bool IsResolved => _site is not null;

    internal void Set(SiteModel site) => _site = site;
}
