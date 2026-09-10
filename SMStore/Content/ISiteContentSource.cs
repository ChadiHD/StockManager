namespace SMStore.Content;

/// <summary>
/// A content page belonging to one store.
/// </summary>
/// <param name="BodyHtml">
/// Rendered as markup, so it must only ever come from a trusted, staff-authored store — never
/// from customer input. The admin portal is the intended author.
/// </param>
public sealed record SiteContentPage(string Key, string Title, string? Lede, string? BodyHtml);

/// <summary>
/// Where a store's page content comes from. The storefront's content pages — Solutions, About,
/// Contact and the legal pages — differ per store and must not be markup in shared components,
/// so they resolve through here.
///
/// <see cref="NullSiteContentSource"/> is the default and returns nothing, which renders the
/// page's empty state. A database-backed source replaces it without touching the pages.
/// </summary>
public interface ISiteContentSource
{
    Task<SiteContentPage?> GetAsync(int siteId, string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Serves no content. Keeps the storefront honest until a real source is wired up: an
/// unconfigured store shows an explicit empty state rather than another store's words.
/// </summary>
public sealed class NullSiteContentSource : ISiteContentSource
{
    public Task<SiteContentPage?> GetAsync(int siteId, string key, CancellationToken cancellationToken = default)
        => Task.FromResult<SiteContentPage?>(null);
}
