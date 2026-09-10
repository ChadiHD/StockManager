namespace SMStore.Sites;

public sealed class SiteOptions
{
    public const string SectionName = "Sites";

    /// <summary>
    /// Forces every request to resolve to this site key, ignoring the host header. Development
    /// only: it is the sole way to exercise a real store's branding, tax rules and content from
    /// localhost without editing the hosts file. Never set outside development — with it set,
    /// one tenant's pages would be served on every domain.
    /// </summary>
    public string? ForceSiteKey { get; set; }

    /// <summary>
    /// How long a resolved host-to-site mapping is cached. Sites change rarely, so this exists
    /// to keep a per-request database round trip off the hot path, not to be tuned.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}
