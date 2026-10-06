using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Security;
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
        public record SiteOption(int Id, string SiteKey, string Name, string Country, string CurrencyCode, bool IsActive);

        [HttpGet]
        public ActionResult<List<SiteOption>> Get()
        {
            // The stores this admin may act for: every one for a user with AllSites — including
            // those not open yet, which only they can set up (T9) — otherwise the open ones they
            // were given. The same answer AdminSiteResolutionMiddleware gives per request, so the
            // selector never offers a store the API would then refuse.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var allSites = _users.GetUserById(userId).FirstOrDefault()?.AllSites == true;
            var given = allSites
                ? null
                : _users.GetSiteGrants(userId).Select(grant => grant.SiteId).ToHashSet();

            var options = _siteData.GetSites()
                .Where(site => given is null || (site.IsActive && given.Contains(site.Id)))
                .OrderBy(site => site.Name)
                .Select(site => new SiteOption(
                    site.Id, site.SiteKey, site.Name, site.Country, site.CurrencyCode, site.IsActive))
                .ToList();

            return Ok(options);
        }

        public record NewStoreModel(
            string? SiteKey, string? Name, string? Domain, string? Country, string? CurrencyCode,
            string? Locale, string? RegistrationFieldSet, string? TaxRuleSet);

        /// <summary>Creates a store, closed, to be configured and then opened (T9).</summary>
        [Authorize(Policy = AllStores.Policy)]
        [HttpPost]
        public IActionResult Create(NewStoreModel store, [FromServices] IMemoryCache cache)
        {
            var key = store.SiteKey?.Trim().ToLowerInvariant() ?? string.Empty;

            // It names the store's theme folder and sits in every admin's stored choice, so it is
            // held to what is safe in a path and a header, and never changes.
            if (!StoreKey.IsMatch(key))
            {
                return BadRequest("The key is lower-case letters, digits and single hyphens, up to 50 characters — such as acme-uk.");
            }

            var site = new SiteModel
            {
                SiteKey = key,
                Name = store.Name!,
                Domain = store.Domain!,
                Country = store.Country!,
                CurrencyCode = store.CurrencyCode!,
                Locale = store.Locale!,
                RegistrationFieldSet = store.RegistrationFieldSet!,
                TaxRuleSet = store.TaxRuleSet!,
                // What spSite_Insert takes from the table's defaults, for the rules to check.
                OrderMode = SiteSettingKeys.OrderModes[0],
                PriceDisplay = SiteSettingKeys.PriceDisplays[0]
            };

            var refusal = SiteSettingsRules.WhyRefused(site);
            if (refusal is not null) return BadRequest(refusal);

            var (id, dbRefusal) = _siteData.CreateSite(site);
            if (dbRefusal is not null) return BadRequest(dbRefusal);

            AdminSiteResolutionMiddleware.Forget(cache, key);

            return Ok(new SiteOption(id, site.SiteKey, site.Name, site.Country, site.CurrencyCode, false));
        }

        /// <summary>Opens a store to customers, once its checklist is met.</summary>
        [Authorize(Policy = AllStores.Policy)]
        [HttpPost("{siteKey}/Open")]
        public IActionResult Open(string siteKey, [FromServices] IMemoryCache cache) => SetActive(siteKey, true, cache);

        /// <summary>Takes a store away from customers; its storefront answers 404.</summary>
        [Authorize(Policy = AllStores.Policy)]
        [HttpPost("{siteKey}/Close")]
        public IActionResult Close(string siteKey, [FromServices] IMemoryCache cache) => SetActive(siteKey, false, cache);

        private IActionResult SetActive(string siteKey, bool open, IMemoryCache cache)
        {
            var site = _siteData.GetSites().FirstOrDefault(candidate => candidate.SiteKey == siteKey);
            if (site is null) return NotFound();

            var refusal = _siteData.SetActive(site.Id, open);
            if (refusal is not null) return Conflict(refusal);

            AdminSiteResolutionMiddleware.Forget(cache, siteKey);

            return NoContent();
        }

        private static readonly System.Text.RegularExpressions.Regex StoreKey =
            new(@"^(?=.{1,50}$)[a-z0-9]+(-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.Compiled);

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
