namespace SMStore.Accounts;

/// <summary>
/// Names and routes for the storefront's own sign-in.
/// </summary>
public static class CustomerAuthentication
{
    /// <summary>
    /// Distinct from <c>IdentityConstants.ApplicationScheme</c>, which StockApi uses for the
    /// Razor admin UI.
    /// </summary>
    public const string Scheme = "SMStore.Customer";

    /// <summary>
    /// The cookie name, and it must not be the Identity default.
    /// </summary>
    /// <remarks>
    /// Cookies are scoped by host and path, never by port. In development StockApi is
    /// localhost:7042 and this is localhost:7260, so they are one origin as far as the
    /// browser is concerned — and the Data Protection ring is shared, so each can decrypt
    /// what the other wrote. Two schemes both called ".AspNetCore.Identity.Application" would
    /// overwrite each other's sessions, and an administrator's cookie would arrive here
    /// looking entirely valid.
    ///
    /// CustomerSessionValidator is what makes that arrival harmless. This just stops the two
    /// sessions from colliding in the first place.
    /// </remarks>
    public const string CookieName = ".SMStore.Customer";

    public const string LoginPath = "/login";
    public const string LogoutPath = "/logout";
    public const string AccessDeniedPath = "/login";
}
