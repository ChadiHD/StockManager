using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StockApi.Security;

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
        // From the form only. Bound by default, a password in the query string was accepted too,
        // and a URL is written to access logs and telemetry where a body is not.
        public async Task<IActionResult> Create(
            [FromForm] string username, [FromForm] string password, [FromForm] string grant_type)
        {
            var user = await FindStaffAsync(username, password);

            // One answer for every refusal: an unknown name, a customer's, a wrong password
            // and a locked-out account must not be told apart from outside.
            if (user is null)
            {
                return BadRequest("Could not create token");
            }

            return new ObjectResult(await StaffToken.IssueAsync(_userManager, _configuration, user));
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
                // A hash a real login would have cost, so the response time does not say which
                // staff names exist — the storefront's sign-in does the same.
                _ = _userManager.PasswordHasher.HashPassword(new IdentityUser(), password);
                return null;
            }

            // Counts failures toward lockout, which CheckPasswordAsync does not.
            var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

            // A locked or unconfirmed login is refused before its password is checked, which
            // is as quick as an unknown name was.
            if (result.IsLockedOut || result.IsNotAllowed)
            {
                _ = _userManager.PasswordHasher.HashPassword(user, password);
            }

            return result.Succeeded ? user : null;
        }
    }
}
