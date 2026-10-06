using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;

namespace StockApi.Controllers
{
    /// <summary>
    /// The stores an admin may act for. Backs the site selector in the portal, which puts the
    /// chosen key in the X-Site-Key header on every subsequent call.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class SiteController : ControllerBase
    {
        private readonly ISiteData _siteData;
        private readonly IUserData _users;

        public SiteController(ISiteData siteData, IUserData users)
        {
            _siteData = siteData;
            _users = users;
        }

        /// <summary>
        /// Identity and display only. The full site row carries commercial configuration —
        /// margin floor, price visibility, ordering mode — that a selector has no use for,
        /// and this endpoint exists to populate a dropdown.
        /// </summary>
        public record SiteOption(int Id, string SiteKey, string Name, string Country, string CurrencyCode);

        [HttpGet]
        public ActionResult<List<SiteOption>> Get()
        {
            // The stores this admin may act for: every one for a user with AllSites, otherwise
            // the ones they were given. The same answer AdminSiteResolutionMiddleware gives per
            // request, so the selector never offers a store the API would then refuse.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var allSites = _users.GetUserById(userId).FirstOrDefault()?.AllSites == true;
            var given = allSites
                ? null
                : _users.GetSiteGrants(userId).Select(grant => grant.SiteId).ToHashSet();

            var options = _siteData.GetSites()
                .Where(site => site.IsActive && (given is null || given.Contains(site.Id)))
                .OrderBy(site => site.Name)
                .Select(site => new SiteOption(
                    site.Id, site.SiteKey, site.Name, site.Country, site.CurrencyCode))
                .ToList();

            return Ok(options);
        }
    }
}
