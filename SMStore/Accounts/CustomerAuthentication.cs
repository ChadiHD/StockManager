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

    /// <summary>
    /// Where the link in the acknowledgement email lands. A page rather than an endpoint,
    /// because the applicant has to be told what happened.
    /// </summary>
    public const string ConfirmEmailPath = "/confirm-email";

    /// <summary>Posts a fresh confirmation link to an address, or pretends to.</summary>
    public const string ResendConfirmationPath = "/resend-confirmation";

    /// <summary>Where someone who cannot remember their password asks for a link.</summary>
    public const string ForgotPasswordPath = "/forgot-password";

    /// <summary>Posts a reset link to an address, or pretends to.</summary>
    /// <remarks>
    /// A separate endpoint rather than a post back to the page, because the page has nothing
    /// to report: the answer is the same for every address and is therefore a redirect, not a
    /// re-render. The reset page below is the opposite case — it holds a token and has real
    /// errors to show — so that one handles its own post.
    /// </remarks>
    public const string RequestPasswordResetPath = "/request-password-reset";

    /// <summary>
    /// Where the link in the reset email lands, and where the new password is posted.
    /// </summary>
    public const string ResetPasswordPath = "/reset-password";

    /// <summary>
    /// Carries the security stamp the login had when the session began.
    /// </summary>
    /// <remarks>
    /// This is what lets a password change end sessions it cannot reach.
    /// <c>ResetPasswordAsync</c> rotates the stamp, so a cookie minted before the reset no
    /// longer matches the login it names, and <c>CustomerSessionValidator</c> drops it on its
    /// next request. Without it, a customer who resets because they think somebody is in their
    /// account changes nothing for the somebody, who stays signed in until the cookie expires.
    ///
    /// Deliberately not <c>IdentityOptions.ClaimsIdentity.SecurityStampClaimType</c>. Both
    /// hosts share a key ring and, in development, a hostname, so an admin cookie written by
    /// StockApi is readable here — under Identity's own claim name it would arrive carrying a
    /// stamp that validates. Under this name it arrives carrying nothing.
    /// </remarks>
    public const string SecurityStampClaim = "SMStore.Customer.SecurityStamp";
}
