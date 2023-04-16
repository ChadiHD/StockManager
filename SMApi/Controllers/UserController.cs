using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using SMDataManager.Library.Models;
using SMDataManager.Library.DataAccess;
using SMApi.Models;
using SMApi.Data;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SMApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IUserData _userData;
        private readonly ILogger<UserController> _logger;

        public UserController(ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            IUserData userData,
            ILogger<UserController> logger)
        {
            _context = context;
            _userManager = userManager;
            _userData = userData;
            _logger = logger;
        }

        [HttpGet]
        // GET: User/Details/First user
        public UserModel GetById()
        {
            // request the userId from the API directly
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier); // Outdated - RequestContext.Principal.Identity.GetUserId();
            
            return _userData.GetUserById(userId).First();
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        [Route("Admin/GetAllUsers")]
        public List<ApplicationUserModel> GetAllUsers()
        {
            List<ApplicationUserModel> output = new List<ApplicationUserModel>();

            // Entity Framework Application context manager
            var users = _context.Users.ToList();
            var userRoles = from ur in _context.UserRoles
                            join r in _context.Roles on ur.RoleId equals r.Id
                            select new { ur.UserId, ur.RoleId, r.Name };

            foreach (var user in users)
            {
                ApplicationUserModel uModel = new ApplicationUserModel
                {
                    Id = user.Id,
                    Email = user.Email,
                };

                uModel.Roles = userRoles.Where(x => x.UserId == uModel.Id).ToDictionary(key => key.RoleId, val => val.Name);

                output.Add(uModel);
            }

            return output;
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        [Route("Admin/GetAllRoles")]
        public Dictionary<string, string> GetAllRoles()
        {
            var roles = _context.Roles.ToDictionary(x => x.Id, x => x.Name);
            return roles;
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [Route("Admin/AddRole")]
        public async Task AddRole(UserRolePairModel pairing)
        {
            string loggedInUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var user = await _userManager?.FindByIdAsync(pairing.UserId);

            _logger.LogInformation("Admin {Admin} added user {User} to role {Role}",
                loggedInUserId, user.Id, pairing.RoleName);

            await _userManager.AddToRoleAsync(user, pairing.RoleName);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete]
        [Route("Admin/RemoveRole")]
        public async Task RemoveRole(UserRolePairModel pairing)
        {
            string loggedInUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var user = await _userManager?.FindByIdAsync(pairing.UserId);

            _logger.LogInformation("Admin {Admin} removed user {User} from role {Role}",
                loggedInUserId, user.Id, pairing.RoleName);

            await _userManager.RemoveFromRoleAsync(user, pairing.RoleName);
        }
    }
}
