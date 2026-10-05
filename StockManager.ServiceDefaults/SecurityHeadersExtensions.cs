using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, string contentSecurityPolicy)
    {
        /*
        In Development, connections to other localhost ports too. Visual Studio injects Browser
        Link and hot reload's browser-refresh script into every page, and both connect back to
        the IDE on a port of their own — refused under connect-src 'self', which broke hot reload
        and filled the console with violations. Development only, and localhost only.
        */
        if (app.ApplicationServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            contentSecurityPolicy = contentSecurityPolicy.Replace(
                "connect-src 'self'",
                "connect-src 'self' http://localhost:* ws://localhost:* wss://localhost:*");
        }

        return app.Use((context, next) =>
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
}
