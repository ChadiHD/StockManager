using SMDataManager.Library.DataAccess;
using SMStore.Sites;

namespace SMStore.Content;

/// <summary>
/// Serves content from dbo.SiteContent, scoped to the requesting store and preferring its
/// locale.
/// </summary>
public sealed class DatabaseSiteContentSource : ISiteContentSource
{
    private readonly ISiteContentData _data;
    private readonly ISiteContext _siteContext;

    public DatabaseSiteContentSource(ISiteContentData data, ISiteContext siteContext)
    {
        _data = data;
        _siteContext = siteContext;
    }

    public Task<SiteContentPage?> GetAsync(int siteId, string key, CancellationToken cancellationToken = default)
    {
        // The caller passes the site it means, but a mismatch would be a cross-tenant read, so
        // it is refused rather than trusted — see the site-scoping rule in CLAUDE.md.
        if (siteId != _siteContext.Site.Id)
        {
            throw new InvalidOperationException(
                $"Refusing to read content for site {siteId} while serving site " +
                $"{_siteContext.Site.Id}.");
        }

        var row = _data.GetByKey(siteId, key, _siteContext.Site.Locale);

        return Task.FromResult(row is null
            ? null
            : new SiteContentPage(row.ContentKey, row.Title, row.Lede, row.BodyHtml));
    }
}
