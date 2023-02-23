using Microsoft.AspNet.Identity;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
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
    }
}
