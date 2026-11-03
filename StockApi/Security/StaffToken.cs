using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace StockApi.Security;

/// <summary>
/// The staff bearer token: what it carries, and the one claim this host compares on every
/// request rather than trusts.
/// </summary>
/// <remarks>
/// A JWT is valid until it expires, whatever happens to the login it names. Until this existed,
/// removing an admin's role or changing their password left every token already issued working
/// for the rest of its day — long enough for a departing admin to register a new login and make
/// it an Admin, which no later change to the old one would reach. So the token carries the
/// login's security stamp, every request compares it with the stamp the login has now, and
/// whatever should end a session rotates the stamp: <c>ChangePasswordAsync</c> does so itself,
/// and <c>UserController.RemoveRole</c> does it explicitly, because Identity's role changes do
/// not.
///
/// It costs one primary-key read on ApiAuthDb per authenticated request, which is the trade the
/// storefront's <c>CustomerSessionValidator</c> already makes for the same reason.
///
/// Lockout is deliberately not checked. A login locks after five wrong passwords from anyone,
/// so refusing a locked login's tokens would let a stranger end an admin's session by typing
/// their name into /token five times.
/// </remarks>
public static class StaffToken
{
    public const string SecurityStampClaim = "StockApi.Staff.SecurityStamp";

    // Minted and required, so a token signed with this key for any other purpose is not a
    // staff token here.
    public const string Issuer = "StockApi";
    public const string Audience = "StockManager.Staff";

    /// <summary>
    /// The HMAC key, read by Program at startup so a short one stops the host rather than
    /// failing the first sign-in after a deploy.
    /// </summary>
    public static byte[] SigningKey(IConfiguration configuration)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("JWT signing key configuration 'Jwt:SigningKey' is missing.");
        var signingKeyBytes = Encoding.UTF8.GetBytes(signingKey);

        if (signingKeyBytes.Length < 32)
        {
            throw new InvalidOperationException("JWT signing key configuration 'Jwt:SigningKey' must contain at least 32 UTF-8 bytes.");
        }

        return signingKeyBytes;
    }

    public static async Task<object> IssueAsync(
        UserManager<IdentityUser> users,
        IConfiguration configuration,
        IdentityUser user)
    {
        // Identity stamps every login it creates, but a login without one would be issued a
        // token this host then refuses on every request, with nothing to say why.
        if (string.IsNullOrEmpty(user.SecurityStamp))
        {
            await users.UpdateSecurityStampAsync(user);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserName!),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(SecurityStampClaim, user.SecurityStamp!),
            new(JwtRegisteredClaimNames.Iss, Issuer),
            new(JwtRegisteredClaimNames.Aud, Audience),
            new(JwtRegisteredClaimNames.Nbf, new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds().ToString()),
            new(JwtRegisteredClaimNames.Exp, new DateTimeOffset(DateTime.Now.AddDays(1)).ToUnixTimeSeconds().ToString())
        };

        claims.AddRange((await users.GetRolesAsync(user)).Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            new JwtHeader(new SigningCredentials(new SymmetricSecurityKey(SigningKey(configuration)), SecurityAlgorithms.HmacSha256)),
            new JwtPayload(claims));

        return new
        {
            Access_Token = new JwtSecurityTokenHandler().WriteToken(token),
            Username = user.UserName
        };
    }

    /// <summary>
    /// <c>JwtBearerEvents.OnTokenValidated</c>: a signature and an expiry prove the token was
    /// issued, not that the login it names still stands behind it.
    /// </summary>
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<IdentityUser>>();

        if (context.Principal is null || !await IsCurrentAsync(context.Principal, users))
        {
            context.Fail("The login this token names has changed since it was issued.");
        }
    }

    /// <remarks>
    /// A missing claim is refused rather than skipped. Tokens issued before this check carry
    /// none, so each ends once and its holder signs in again; treating an absent claim as
    /// nothing to compare would leave a day-long way round the check it exists to make.
    /// </remarks>
    public static async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, UserManager<IdentityUser> users)
    {
        var issuedWith = principal.FindFirstValue(SecurityStampClaim);
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(issuedWith) || string.IsNullOrEmpty(userId))
        {
            return false;
        }

        var user = await users.FindByIdAsync(userId);

        // The property rather than GetSecurityStampAsync, which throws for a login with no stamp:
        // a 500 on every call, where this is one sign-in prompt.
        return !string.IsNullOrEmpty(user?.SecurityStamp)
            && string.Equals(user.SecurityStamp, issuedWith, StringComparison.Ordinal);
    }
}
