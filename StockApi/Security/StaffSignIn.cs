using System.Globalization;
using System.Threading.RateLimiting;
using StockManager.Identity;

namespace StockApi.Security;

/// <summary>
/// The rules for who may sign in to this host, which is staff only.
/// </summary>
/// <remarks>
/// Staff and customers share one Identity user store, and /token used to find a caller by email
/// with CheckPasswordAsync — so a customer's storefront password worked here, with no lockout
/// and no rate limit, which made the storefront's sign-in cap pointless. A customer login is
/// always site-qualified ({SiteKey}|{email}) and a staff login never is, so the name alone
/// says which side of the store a caller is on, before any password is checked.
/// </remarks>
public static class StaffSignIn
{
    public const string RateLimitPolicy = "staff-sign-in";

    /// <summary>
    /// The storefront sign-in's numbers, for the storefront sign-in's reason: a person who
    /// mistypes retries at once, and a guessing script wants thousands.
    /// </summary>
    private const int SignInsPerFiveMinutes = 20;

    /// <summary>
    /// Whether a login name could belong to staff. Customer names carry the separator, and no
    /// staff name may — <c>UserController.Register</c> refuses one.
    /// </summary>
    public static bool IsStaffName(string? userName) =>
        !string.IsNullOrWhiteSpace(userName)
        && !userName.Contains(SiteQualifiedUserName.Separator);

    public static IServiceCollection AddStaffRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Per IP, which is the ingress's address until forwarded headers are on; see the
            // T7 plan, item 10.
            options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = SignInsPerFiveMinutes,
                    Window = TimeSpan.FromMinutes(5)
                }));

            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
        });
}
