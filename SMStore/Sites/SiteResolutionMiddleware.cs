namespace SMStore.Sites;

/// <summary>
/// Resolves the store for the request and fails the request outright when it cannot. An
/// unresolved host must not fall through to a default store: that is how one tenant's catalog
/// and prices end up served on another tenant's domain.
/// </summary>
public sealed class SiteResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SiteResolver _resolver;

    public SiteResolutionMiddleware(RequestDelegate next, SiteResolver resolver)
    {
        _next = next;
        _resolver = resolver;
    }

    public async Task InvokeAsync(HttpContext context, SiteContext siteContext)
    {
        var site = _resolver.Resolve(context.Request.Host.Host);

        if (site is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("No store is configured for this address.");
            return;
        }

        siteContext.Set(site);

        await _next(context);
    }
}

public static class SiteResolutionMiddlewareExtensions
{
    /// <summary>
    /// Must run before static assets, endpoints and antiforgery — per-site theme files are
    /// served by key, and every endpoint downstream assumes a resolved site.
    /// </summary>
    public static IApplicationBuilder UseSiteResolution(this IApplicationBuilder app)
        => app.UseMiddleware<SiteResolutionMiddleware>();
}
