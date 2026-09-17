using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using SMDataManager.Library.Models;

namespace SMStore.Accounts;

/// <summary>
/// Builds and reads the links that carry an ASP.NET Identity token to a customer's inbox —
/// email confirmation, and password reset.
/// </summary>
/// <remarks>
/// The tokens themselves are Identity's, which is what makes them single-use and expiring
/// without this codebase inventing either: both <c>ConfirmEmailAsync</c> and
/// <c>ResetPasswordAsync</c> validate against the user's security stamp and rotate it on
/// success, so neither link can be replayed. That is what the T3 plan asks for, and it is the
/// reason nothing here rolls its own.
///
/// One class for both because the two rules that matter are the same rules, and written twice
/// they would drift: the host comes from the site and never from the request, and the token is
/// base64url on the way out.
/// </remarks>
public static class MailedTokenLink
{
    public const string UserIdParameter = "userId";
    public const string TokenParameter = "token";

    /// <summary>
    /// The absolute link to mail, built from the store's own domain.
    /// </summary>
    /// <remarks>
    /// From <c>Site.Domain</c> and not from the current request. A link assembled out of the
    /// request host would be assembled out of a header — and this one arrives in a customer's
    /// inbox, where a link to an attacker's host carrying a valid token is the whole prize.
    /// That matters more for a reset than for a confirmation: the confirmation token proves
    /// an address, the reset token hands over the account.
    /// </remarks>
    public static string For(SiteModel site, string path, string identityUserId, string token) =>
        $"https://{site.Domain}{path}" +
        $"?{UserIdParameter}={Uri.EscapeDataString(identityUserId)}" +
        $"&{TokenParameter}={Encode(token)}";

    /// <summary>
    /// Base64url, because Identity's tokens contain characters a query string mangles and a
    /// token that survives one mail client and not the next is worse than no token.
    /// </summary>
    public static string Encode(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    /// <summary>Null for anything that is not a token this code encoded.</summary>
    public static string? TryDecode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            // A hand-edited or truncated link. Decoding is the first thing that touches
            // attacker-supplied text here, so it fails to null rather than throwing into a 500.
            return null;
        }
    }
}
