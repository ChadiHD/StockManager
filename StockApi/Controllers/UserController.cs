using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockManager.Identity;
using StockApi.Models;
using StockApi.Security;
using System.Security.Claims;

namespace StockApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IUserData _userData;
        private readonly ILogger<UserController> _logger;
        private readonly IConfiguration _configuration;
        private readonly SignInManager<IdentityUser> _signInManager;

        public UserController(ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            IUserData userData,
            ILogger<UserController> logger,
            IConfiguration configuration,
            SignInManager<IdentityUser> signInManager)
        {
            _context = context;
            _userManager = userManager;
            _userData = userData;
            _logger = logger;
            _configuration = configuration;
            _signInManager = signInManager;
        }

        [HttpGet]
		// GET: User/Details/First user
		public ActionResult<UserModel> GetById()
        {
            // request the userId from the API directly
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var user = _userData.GetUserById(userId).FirstOrDefault();
            if (user is null)
            {
                // Authenticated (valid token) but no matching SMDatabase profile row.
                return NotFound();
            }

            return user;
        }

        public record UserRegistrationModel(
            string FirstName,
            string LastName,
            string Email,
            string Password);

        // Admin only. It was anonymous, so anyone could add a confirmed login to the staff list,
        // and its 409 — looked up by email across the whole user store — told a stranger which
        // addresses were customers of any store here.
        //
        // Staff are not one store's, so managing them takes an admin of every store (T9); an
        // admin given one store could otherwise give themselves the rest.
        [Authorize(Roles = "Admin", Policy = AllStores.Policy)]
        [HttpPost]
        [Route("Register")]
        // POST: User/Register
        public async Task<IActionResult> Register(UserRegistrationModel user)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // A staff name is never site-qualified; that is how /token tells staff from
            // customers. See StaffSignIn.
            if (!StaffSignIn.IsStaffName(user.Email))
            {
                return BadRequest($"A staff email address cannot contain '{SiteQualifiedUserName.Separator}'.");
            }

            // By name, which is the email for staff: customers' logins are named differently, so
            // a customer's address neither collides with this nor can be probed through it.
            var existingUser = await _userManager.FindByNameAsync(user.Email);
            if (existingUser is not null)
            {
                return Conflict("A user with this email address already exists.");
            }

            IdentityUser newUser = new()
            {
                Email = user.Email,
                EmailConfirmed = true,
                UserName = user.Email
            };

            IdentityResult result = await _userManager.CreateAsync(newUser, user.Password);
            if (!result.Succeeded)
            {
                // Sentences, not IdentityError objects: the portal shows this to the admin who
                // typed the password, and they need to read which rule it broke.
                return BadRequest(Describe(result));
            }

            try
            {
                // The profile row lives in SMDatabase, a *different* database than Identity
                // (ApiAuthDb), so a single transaction cannot span both writes. If this write
                // fails, compensate by deleting the Identity user we just created — otherwise
                // an orphaned account is left that can authenticate but has no profile row.
                _userData.CreateUser(new UserModel
                {
                    UserId = newUser.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    EmailAddress = user.Email
                });
            }
            catch
            {
                await _userManager.DeleteAsync(newUser);
                throw;
            }

            // The id, so the portal grants roles and stores to this login by id. It used to look
            // the new user up again by email, and a customer login sharing the address could be
            // the one it found.
            return Ok(new { userId = newUser.Id });
        }

        public record PasswordChangeModel(string CurrentPassword, string NewPassword);

        // Any member of staff, for their own login only — the user comes from the token, never
        // from the request. An admin sets a new colleague's first password, so until this is
        // used two people know it.
        //
        // It checks a password, so it is limited and counts failures toward lockout like /token
        // does. Without both, a stolen token was an unthrottled way to guess the current password
        // and then change it, keeping the login after the token expired.
        [Authorize(Roles = "Admin,Manager,Staff")]
        [HttpPut]
        [Route("Me/Password")]
        [EnableRateLimiting(StaffSignIn.RateLimitPolicy)]
        public async Task<IActionResult> ChangeOwnPassword(PasswordChangeModel change)
        {
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = string.IsNullOrEmpty(userId) ? null : await _userManager.FindByIdAsync(userId);

            if (user is null)
            {
                return Unauthorized();
            }

            var current = await _signInManager.CheckPasswordSignInAsync(
                user, change.CurrentPassword ?? string.Empty, lockoutOnFailure: true);

            if (!current.Succeeded)
            {
                return BadRequest(current.IsLockedOut
                    ? "Too many wrong passwords. Try again later."
                    : "Your current password is not right.");
            }

            var result = await _userManager.ChangePasswordAsync(
                user, change.CurrentPassword ?? string.Empty, change.NewPassword ?? string.Empty);

            if (!result.Succeeded)
            {
                return BadRequest(Describe(result));
            }

            _logger.LogInformation("User {User} changed their password.", user.Id);

            // The change rotated the security stamp, which ended every session this login had
            // open, the caller's included. Answering with a token for the new password keeps the
            // caller signed in; StaffToken refuses the rest.
            return Ok(await StaffToken.IssueAsync(_userManager, _configuration, user));
        }

        private static string Describe(IdentityResult result) =>
            string.Join(" ", result.Errors.Select(error => error.Description));

        // Staff only: the logins with a profile in dbo.User. This returned every Identity login,
        // which since T3 includes every customer of every store, to the users screen.
        [Authorize(Roles = "Admin", Policy = AllStores.Policy)]
        [HttpGet]
        [Route("Admin/GetAllUsers")]
        public List<ApplicationUserModel> GetAllUsers()
        {
            List<ApplicationUserModel> output = new();

            var staff = _userData.GetAllUsers().ToDictionary(profile => profile.UserId);
            var grants = _userData.GetSiteGrants()
                .GroupBy(grant => grant.UserId)
                .ToDictionary(group => group.Key, group => group.Select(grant => grant.SiteId).ToList());

            // Entity Framework Application context manager
            var staffIds = staff.Keys.ToList();
            var users = _context.Users.Where(user => staffIds.Contains(user.Id)).ToList();
            var userRoles = from ur in _context.UserRoles
                            join r in _context.Roles on ur.RoleId equals r.Id
                            select new { ur.UserId, ur.RoleId, r.Name };

            foreach (var user in users)
            {
                ApplicationUserModel uModel = new()
                {
                    UserId = user.Id,
                    Email = user.Email,
                    AllSites = staff[user.Id].AllSites,
                    SiteIds = grants.TryGetValue(user.Id, out var siteIds) ? siteIds : new List<int>()
                };

                uModel.Roles = userRoles.Where(x => x.UserId == uModel.UserId).ToDictionary(key => key.RoleId, val => val.Name);

                output.Add(uModel);
            }

            return output;
        }

        // Staff profiles from SMDatabase (display names). Identity holds the logins and roles;
        // the portal joins the two so the users page can show a name next to each account.
        [Authorize(Roles = "Admin", Policy = AllStores.Policy)]
        [HttpGet]
        [Route("Admin/Staff")]
        public List<UserModel> GetAllStaff()
        {
            return _userData.GetAllUsers();
        }

        public record StoreAccessModel(bool AllSites, List<int>? SiteIds);

        // Which stores a member of staff may act for in the portal: every one, or exactly these.
        [Authorize(Roles = "Admin", Policy = AllStores.Policy)]
        [HttpPut]
        [Route("Admin/{userId}/Stores")]
        public async Task<IActionResult> SetStores(string userId, StoreAccessModel access)
        {
            if (_userData.GetUserById(userId).Count == 0)
            {
                return NotFound();
            }

            if (!access.AllSites && await IsLastAdminOfEveryStoreAsync(userId))
            {
                return Conflict(LastAdminOfEveryStore);
            }

            _userData.SetSiteAccess(userId, access.AllSites, access.SiteIds ?? new List<int>());

            _logger.LogInformation("Admin {Admin} set user {User}'s stores to {Stores}.",
                User.FindFirstValue(ClaimTypes.NameIdentifier), userId,
                access.AllSites ? "all" : string.Join(",", access.SiteIds ?? new List<int>()));

            return NoContent();
        }

        private const string LastAdminOfEveryStore =
            "They are the only admin who can manage every store. Give another admin every store first, " +
            "or nobody will be able to manage staff or add a store.";

        // Somebody has to be able to hand out stores. Roles live in ApiAuthDb and AllSites in
        // SMDatabase, so the check is here rather than in spUserSite_Set.
        private async Task<bool> IsLastAdminOfEveryStoreAsync(string userId) =>
            IsLastAdminOfEveryStore(
                userId,
                (await _userManager.GetUsersInRoleAsync(AdminBootstrap.AdminRole)).Select(admin => admin.Id),
                _userData.GetAllUsers().Where(profile => profile.AllSites).Select(profile => profile.UserId));

        /// <summary>
        /// Whether <paramref name="userId"/> is the only user who is both an Admin and may act
        /// for every store, so that taking either away leaves nobody able to manage staff.
        /// </summary>
        public static bool IsLastAdminOfEveryStore(
            string userId, IEnumerable<string> adminIds, IEnumerable<string> everyStoreIds)
        {
            var everyStore = everyStoreIds.ToHashSet();
            var both = adminIds.Where(everyStore.Contains).Distinct().ToList();

            return both.Count == 1 && both[0] == userId;
        }

        [Authorize(Roles = "Admin", Policy = AllStores.Policy)]
        [HttpGet]
        [Route("Admin/GetAllRoles")]
        public Dictionary<string, string> GetAllRoles()
        {
            var roles = _context.Roles.ToDictionary(x => x.Id, x => x.Name);
            return roles;
        }

        [Authorize(Roles = "Admin", Policy = AllStores.Policy)]
        [HttpPost]
        [Route("Admin/AddRole")]
        public async Task<IActionResult> AddRole(UserRolePairModel pairing)
        {
            string? loggedInUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var user = string.IsNullOrEmpty(pairing.UserId) ? null : await _userManager.FindByIdAsync(pairing.UserId);

            if (user is null)
            {
                _logger.LogWarning("Admin {Admin} attempted to add a non-existent user {User} to role {Role}",
                    loggedInUserId, pairing.UserId, pairing.RoleName);
                return NotFound();
            }

            // Staff roles, to staff logins. The id arrives in the request, and a customer login
            // holding Admin is harmless only while /token refuses its name — one check standing
            // between a customer and the whole portal.
            if (!StaffSignIn.IsStaffName(user.UserName))
            {
                return BadRequest("Only a staff login can hold a staff role.");
            }

            if (!AdminBootstrap.StaffRoles.Contains(pairing.RoleName, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest($"'{pairing.RoleName}' is not a staff role.");
            }

            var added = await _userManager.AddToRoleAsync(user, pairing.RoleName!);

            if (!added.Succeeded)
            {
                return BadRequest(Describe(added));
            }

            _logger.LogInformation("Admin {Admin} added user {User} to role {Role}",
                loggedInUserId, user.Id, pairing.RoleName);

            return NoContent();
        }

        [Authorize(Roles = "Admin", Policy = AllStores.Policy)]
        [HttpDelete]
        [Route("Admin/RemoveRole")]
        public async Task<IActionResult> RemoveRole(UserRolePairModel pairing)
        {
            string? loggedInUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var user = string.IsNullOrEmpty(pairing.UserId) ? null : await _userManager.FindByIdAsync(pairing.UserId);

            if (user is null || string.IsNullOrEmpty(pairing.RoleName))
            {
                _logger.LogWarning("Admin {Admin} attempted to remove a non-existent user {User} from role {Role}",
                    loggedInUserId, pairing.UserId, pairing.RoleName);
                return NotFound();
            }

            if (string.Equals(pairing.RoleName, AdminBootstrap.AdminRole, StringComparison.OrdinalIgnoreCase)
                && await IsLastAdminOfEveryStoreAsync(user.Id))
            {
                return Conflict(LastAdminOfEveryStore);
            }

            var removed = await _userManager.RemoveFromRoleAsync(user, pairing.RoleName);

            if (!removed.Succeeded)
            {
                return BadRequest(Describe(removed));
            }

            // The role is still a claim in every token this login already holds. Identity does
            // not rotate the stamp on a role change, and rotating it is what takes them back.
            await _userManager.UpdateSecurityStampAsync(user);

            _logger.LogInformation("Admin {Admin} removed user {User} from role {Role}",
                loggedInUserId, user.Id, pairing.RoleName);

            return NoContent();
        }
    }
}
