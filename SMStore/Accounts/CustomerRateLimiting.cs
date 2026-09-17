using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SMStore.Registration;

namespace SMStore.Accounts;

/// <summary>
/// Caps how often one caller may post the storefront's unauthenticated write paths.
/// </summary>
/// <remarks>
/// Registration, sign-in and the two halves of password reset are the only things a stranger
/// can POST here, and each is worth limiting for its own reason. Registration creates an
/// Identity user, an account, a contact, an address and up to four uploaded files per request,
/// so it is the cheapest way to fill this database from outside. Sign-in has no lockout at
/// all: the storefront authenticates with <c>UserManager.CheckPasswordAsync</c>, which —
/// unlike <c>SignInManager</c> — does not consult <c>IdentityOptions.Lockout</c>, so nothing
/// else on this host slows a password guess down. Asking for a reset link sends mail to an
/// address the caller chose, which is someone else's inbox as far as this host knows.
///
/// Everything else is unlimited. A global limiter that applied to the catalog would be a
/// denial-of-service switch aimed at the shop window, and the pages a crawler hits hardest are
/// exactly the ones that must stay fast.
/// </remarks>
public static class CustomerRateLimiting
{
    /// <summary>
    /// Registration is capped hard because it writes across two databases and accepts files.
    /// </summary>
    /// <remarks>
    /// Ten an hour is generous for a human applying once and miserly for a script. It is per
    /// IP rather than per site: partitioning by site as well would hand an attacker one
    /// allowance per store, which is the opposite of what a cap is for.
    ///
    /// The office-NAT case is the reason it is not lower. A dozen colleagues behind one address
    /// share this allowance, and B2B applications are rare enough per company that ten still
    /// leaves room — but on a platform whose tenants are big enough for that to bite, this is
    /// the number to revisit first.
    /// </remarks>
    private const int RegistrationsPerHour = 10;

    /// <summary>
    /// Sign-in gets a shorter window: a person who mistypes a password retries immediately,
    /// and a guessing script wants thousands rather than dozens.
    /// </summary>
    private const int SignInsPerFiveMinutes = 20;

    /// <summary>
    /// Asking for a reset link is capped like registration, because it has registration's
    /// shape: unauthenticated, and it sends mail to whatever address it was given.
    /// </summary>
    /// <remarks>
    /// Uncapped it is a mail cannon pointed at one customer's inbox, from this store's own
    /// sending domain — the reputational cost lands here, not on whoever fired it. It is also
    /// the only endpoint on the storefront that will send mail to an address chosen by a
    /// stranger, since registration at least demands a form's worth of company details first.
    /// </remarks>
    private const int PasswordResetRequestsPerHour = 10;

    /// <summary>
    /// Posting a new password gets sign-in's allowance, for sign-in's reason: somebody who
    /// trips the password rules retries at once, and nothing else here rate-limits a
    /// credential-bearing POST.
    /// </summary>
    private const int PasswordResetsPerFiveMinutes = 20;

    public static IServiceCollection AddCustomerRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            /*
            One partitioned limiter rather than named policies, because the paths that need
            limiting are not all endpoints.

            Sign-in is a minimal API and could carry RequireRateLimiting. Registration is a
            Blazor static-SSR form handled by the Razor Components endpoint, which serves every
            page on the site — attaching a policy there would limit the whole storefront. So the
            partition is chosen from the request itself and returns no limiter for anything else.
            */
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!HttpMethods.IsPost(context.Request.Method))
                {
                    return RateLimitPartition.GetNoLimiter("read");
                }

                var path = context.Request.Path;

                if (path.StartsWithSegments(RegistrationPaths.RegisterPath))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"register:{Caller(context)}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = RegistrationsPerHour,
                            Window = TimeSpan.FromHours(1)
                        });
                }

                if (path.StartsWithSegments(CustomerAuthentication.LoginPath))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"signin:{Caller(context)}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = SignInsPerFiveMinutes,
                            Window = TimeSpan.FromMinutes(5)
                        });
                }

                if (path.StartsWithSegments(CustomerAuthentication.RequestPasswordResetPath))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"reset-request:{Caller(context)}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PasswordResetRequestsPerHour,
                            Window = TimeSpan.FromHours(1)
                        });
                }

                if (path.StartsWithSegments(CustomerAuthentication.ResetPasswordPath))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"reset:{Caller(context)}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PasswordResetsPerFiveMinutes,
                            Window = TimeSpan.FromMinutes(5)
                        });
                }

                return RateLimitPartition.GetNoLimiter("other");
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                /*
                A body is written deliberately, and not only for the reader's benefit.

                UseStatusCodePagesWithReExecute re-executes to /not-found for any 400-599 that
                has no body, so a silent 429 would tell the customer this page does not exist.
                Writing anything at all suppresses that.
                */
                await context.HttpContext.Response.WriteAsync(
                    "Too many attempts from this connection. Please wait a few minutes and try again.",
                    cancellationToken);
            };
        });

    /// <summary>
    /// What counts as one caller.
    /// </summary>
    /// <remarks>
    /// The connection's remote address, which is the proxy's address once there is a proxy in
    /// front of this. **T7 must configure forwarded headers before relying on these limits in
    /// production**, or every customer shares one partition and the cap becomes a
    /// denial-of-service against the whole store rather than a defence for it. Unknown
    /// addresses share a partition rather than escaping the limit.
    /// </remarks>
    private static string Caller(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
