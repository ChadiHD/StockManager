using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using SMDataManager.Library.Models;

namespace SMStore.Accounts;

/// <summary>
/// Builds and reads the confirmation link that proves an applicant controls the address they
/// applied with.
/// </summary>
/// <remarks>
/// The token itself is ASP.NET Identity's, which makes it single-use and expiring without this
/// codebase inventing either: <c>ConfirmEmailAsync</c> validates it against the user's security
/// stamp, and confirming the address changes that stamp, so the same link cannot be replayed.
/// That is what the T3 plan asks for, and it is the reason nothing here rolls its own.
///
/// Base64url on the way out because Identity's tokens contain characters a query string
/// mangles, and a token that survives one mail client and not the next is worse than no token.
/// </remarks>
public static class EmailConfirmationLink
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
    /// </remarks>
    public static string For(SiteModel site, string identityUserId, string token) =>
        $"https://{site.Domain}{CustomerAuthentication.ConfirmEmailPath}" +
        $"?{UserIdParameter}={Uri.EscapeDataString(identityUserId)}" +
        $"&{TokenParameter}={Encode(token)}";

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
