using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SMApi.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace SMApi.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<IdentityUser> _userManager;

        public HomeController(ILogger<HomeController> logger,
            RoleManager<IdentityRole> roleManager,
            UserManager<IdentityUser> userManager)
        {
            _logger = logger;
            _roleManager = roleManager;
            _userManager = userManager;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Privacy()
        {
            //This is a method to create roles within EFCore and test the application startup

            //string[] roles = { "Manager", "Admin", "Staff" };
            //foreach (string role in roles)
            //{
            //    var roleExist = await _roleManager.RoleExistsAsync(role);
            //    if (!roleExist)
            //    {
            //        await _roleManager.CreateAsync(new IdentityRole(role));
            //    }
            //}

            //var user = await _userManager.FindByEmailAsync("chadi.hammoud@gmail.com");

            //if (user != null)
            //{
            //    await _userManager.AddToRoleAsync(user, "Admin");
            //}

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
