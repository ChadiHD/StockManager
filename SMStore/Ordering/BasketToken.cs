using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace SMStore.Ordering;

/// <summary>
/// The bearer credential that names an anonymous visitor's basket.
/// </summary>
/// <remarks>
/// A basket has to be findable across requests before anybody signs in, and the two ways to do
/// that are a cookie or client storage. This storefront is static SSR with no interactive
/// render mode and no JavaScript of its own, so client storage would mean writing some —
/// plus a round trip to render the basket, plus a handoff at sign-in where the browser posts a
/// list of product ids the server has to distrust anyway. A cookie carrying an opaque id needs
/// none of that, and the contents stay somewhere the customer cannot edit.
///
/// The cost is that the token *is* the authorisation: whoever holds it holds the basket. So it
/// is 256 bits from a cryptographic RNG rather than a row id or a GUID, and a value that does
/// not have that exact shape is treated as absent rather than looked up — a caller cannot
/// spray guesses that create basket rows.
/// </remarks>
public static class BasketToken
{
    private const int Bytes = 32;

    /// <summary>Base64url of 32 bytes: 43 characters, no padding.</summary>
    private const int Length = 43;

    /// <summary>
    /// The cookie the token travels in. Distinct from the session cookie because the two have
    /// different lifetimes and different meanings — a basket outlives a session on purpose.
    /// </summary>
    public const string CookieName = ".SMStore.Basket";

    /// <summary>
    /// How long the cookie lives, and it matches <c>spBasket_PurgeAbandoned</c>'s default
    /// window. A cookie outliving the row it names would send customers back to a basket that
    /// has been swept; a row outliving the cookie is a row nothing can ever reach again.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static string Mint() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(Bytes));

    /// <summary>
    /// Whether a value from a cookie is shaped like a token this application minted.
    /// </summary>
    /// <remarks>
    /// Not a security boundary on its own — a valid shape still has to match a row — but it
    /// keeps arbitrary cookie content out of the query and, more usefully, means a malformed
    /// cookie produces a fresh basket instead of a lookup for a value nothing can ever match.
    /// </remarks>
    public static bool IsWellFormed(string? value) =>
        value is { Length: Length } && value.All(IsBase64UrlCharacter);

    private static bool IsBase64UrlCharacter(char character) =>
        character is >= 'A' and <= 'Z'
        || character is >= 'a' and <= 'z'
        || character is >= '0' and <= '9'
        || character is '-' or '_';
}
