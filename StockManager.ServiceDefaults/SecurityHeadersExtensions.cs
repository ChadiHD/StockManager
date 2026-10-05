using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.Hosting;

public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Adds the response headers both hosts send on everything: no MIME sniffing, a referrer
    /// that never carries a path off-site, and the host's own Content-Security-Policy.
    /// </summary>
    /// <remarks>
    /// Set in OnStarting rather than before the next middleware runs, because the exception
    /// handler clears the response headers when it re-executes — an error page is the last
    /// place to lose them.
    ///
    /// The policy is per host because the hosts differ: the storefront runs no inline script and
    /// no WebAssembly, the portal needs 'wasm-unsafe-eval'. Both refuse framing, so neither can be
    /// laid under another site's page to have its buttons clicked.
    /// </remarks>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, string contentSecurityPolicy) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers.ContentSecurityPolicy = contentSecurityPolicy;

                return Task.CompletedTask;
            });

            return next(context);
        });
}
