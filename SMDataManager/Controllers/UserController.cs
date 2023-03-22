using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;

namespace SMDataManager.Controllers
{
    [Authorize]
    public class UserController : ApiController
    {
        [HttpGet]
        // GET: User/Details/First user
        public UserModel GetById()
        {
            // request the userId from the API directly (much more secure)
            string userId = RequestContext.Principal.Identity.GetUserId();
            UserData data = new UserData();

            return data.GetUserById(userId).First();
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        [Route("api/User/Admin/GetAllUsers")]
        public List<ApplicationUserModel> GetAllUsers()
        {
            List<ApplicationUserModel> output = new List<ApplicationUserModel>();

            // Entity Framework Application context manager
            using (var context = new ApplicationDbContext())
            {
                var userStore = new UserStore<ApplicationUser>(context);
                var userManager = new UserManager<ApplicationUser>(userStore);

                var users = userManager.Users.ToList();
                var roles = context.Roles.ToList();

                foreach (var user in users)
                {
                    ApplicationUserModel uModel = new ApplicationUserModel
                    {
                        Id = user.Id,
                        Email = user.Email,
                    };

                    foreach (var role in user.Roles)
                    {
                        uModel.Roles.Add(role.RoleId, roles.Where(x => x.Id == role.RoleId).First().Name);
                    }

                    output.Add(uModel);
                }
            }

            return output;
        }
    }
}
