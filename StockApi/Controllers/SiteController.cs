using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Sites;

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

        /// <summary>
        /// The acting store's settings, with the values each keyed setting may take (T9).
        /// </summary>
        public record SettingsView(
            SiteSettingsModel Site,
            bool CanChangeDomain,
            IReadOnlyList<string> OrderModes,
            IReadOnlyList<string> RegistrationFieldSets,
            IReadOnlyList<string> TaxRuleSets,
            IReadOnlyList<string> PriceDisplays);

        [HttpGet("Settings")]
        public ActionResult<SettingsView> GetSettings([FromServices] IAdminSiteContext site)
        {
            var settings = _siteData.GetSettings(site.SiteId);

            return settings is null
                ? NotFound()
                : new SettingsView(settings, ManagesEveryStore(),
                    SiteSettingKeys.OrderModes, SiteSettingKeys.RegistrationFieldSets,
                    SiteSettingKeys.TaxRuleSets, SiteSettingKeys.PriceDisplays);
        }

        [HttpPut("Settings")]
        public IActionResult UpdateSettings(
            SiteModel edited, [FromServices] IAdminSiteContext site, [FromServices] IMemoryCache cache)
        {
            var current = _siteData.GetSettings(site.SiteId);
            if (current is null) return NotFound();

            // Whatever the body says, it is the acting store's row that changes, under its own key.
            edited.Id = current.Id;
            edited.SiteKey = current.SiteKey;

            // The domain is where customers find the store, and DNS and the platform's domain
            // binding have to move with it, so it is an admin of every store's change to make.
            if (!string.Equals(edited.Domain?.Trim(), current.Domain, StringComparison.OrdinalIgnoreCase)
                && !ManagesEveryStore())
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Only an admin of every store can change a store's domain.");
            }

            var refusal = SiteSettingsRules.WhyRefused(edited) ?? _siteData.UpdateSettings(edited);
            if (refusal is not null) return BadRequest(refusal);

            AdminSiteResolutionMiddleware.Forget(cache, current.SiteKey);

            return NoContent();
        }

        private bool ManagesEveryStore()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            return _users.GetUserById(userId).FirstOrDefault()?.AllSites == true;
        }
    }
}
