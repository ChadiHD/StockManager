namespace SMStore.Sites;

/// <summary>
/// Per-site asset paths. The shared stylesheet carries all structure and consumes CSS custom
/// properties only; a theme supplies the values. Nothing about a store's appearance belongs in
/// app.css.
/// </summary>
/// <param name="LogoInversePath">
/// For the footer and any other panel painted in the dark accent. A single logo cannot serve
/// both grounds — the default mark is dark ink, which vanishes on the footer — so a theme
/// supplies a light variant. Falls back to <paramref name="LogoPath"/> when it does not, which
/// is visibly wrong rather than silently missing, and so gets noticed.
/// </param>
public sealed record SiteTheme(
    string StylesheetPath,
    string LogoPath,
    string LogoInversePath,
    string FaviconPath);

/// <summary>
/// Resolves theme asset paths under wwwroot/sites/{SiteKey}/, falling back to the shipped
/// "default" theme for any file a site has not supplied. The fallback is a convenience for a
/// half-configured store in development — a live store should own every asset.
/// </summary>
public sealed class SiteThemeResolver
{
    private const string FallbackSiteKey = "default";

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SiteThemeResolver> _logger;

    public SiteThemeResolver(IWebHostEnvironment environment, ILogger<SiteThemeResolver> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public SiteTheme Resolve(string siteKey) => new(
        Asset(siteKey, "theme.css"),
        Asset(siteKey, "logo.svg"),
        Asset(siteKey, "logo-inverse.svg"),
        Asset(siteKey, "favicon.svg"));

    private string Asset(string siteKey, string fileName)
    {
        var relative = $"sites/{siteKey}/{fileName}";

        if (_environment.WebRootFileProvider.GetFileInfo(relative).Exists)
        {
            return "/" + relative;
        }

        _logger.LogWarning(
            "Site {SiteKey} has no {FileName}; falling back to the default theme.",
            siteKey, fileName);

        return $"/sites/{FallbackSiteKey}/{fileName}";
    }
}
