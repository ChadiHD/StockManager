using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using StockApi.Security;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace StockApi.Controllers
{
    public class TokenController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IConfiguration _configuration;

        public TokenController(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
        }

        [Route("/token")]
        [HttpPost]
        [EnableRateLimiting(StaffSignIn.RateLimitPolicy)]
        public async Task<IActionResult> Create(string username, string password, string grant_type)
        {
            var user = await FindStaffAsync(username, password);

            // One answer for every refusal: an unknown name, a customer's, a wrong password
            // and a locked-out account must not be told apart from outside.
            if (user is null)
            {
                return BadRequest("Could not create token");
            }

            return new ObjectResult(await GenerateToken(user));
        }

        private async Task<IdentityUser?> FindStaffAsync(string username, string password)
        {
            if (!StaffSignIn.IsStaffName(username) || string.IsNullOrEmpty(password))
            {
                return null;
            }

            // By name, not by email. Emails are not unique in this store — a customer of two
            // shops has two logins with one address — and FindByEmailAsync throws on that,
            // which answered an admin sharing an address with a customer with a 500.
            var user = await _userManager.FindByNameAsync(username);

            if (user is null)
            {
                return null;
            }

            // Counts failures toward lockout, which CheckPasswordAsync does not.
            var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

            return result.Succeeded ? user : null;
        }

        private async Task<dynamic> GenerateToken(IdentityUser user)
        {
            var username = user.UserName!;
            var roles = await _userManager.GetRolesAsync(user);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(JwtRegisteredClaimNames.Nbf, new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds().ToString()),
                new Claim(JwtRegisteredClaimNames.Exp, new DateTimeOffset(DateTime.Now.AddDays(1)).ToUnixTimeSeconds().ToString())
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var signingKey = _configuration["Jwt:SigningKey"]
                ?? throw new InvalidOperationException("JWT signing key configuration 'Jwt:SigningKey' is missing.");
            var signingKeyBytes = Encoding.UTF8.GetBytes(signingKey);

            if (signingKeyBytes.Length < 32)
            {
                throw new InvalidOperationException("JWT signing key configuration 'Jwt:SigningKey' must contain at least 32 UTF-8 bytes.");
            }

            var token = new JwtSecurityToken(
                new JwtHeader(
                    new SigningCredentials(
                        new SymmetricSecurityKey(signingKeyBytes),
                        SecurityAlgorithms.HmacSha256)),
                new JwtPayload(claims));

            var output = new
            {
                Access_Token = new JwtSecurityTokenHandler().WriteToken(token),
                Username = username
            };

            return output;
        }
    }
}
