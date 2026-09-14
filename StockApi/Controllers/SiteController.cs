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

        public SiteController(ISiteData siteData)
        {
            _siteData = siteData;
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
            // Every admin sees every store. That is the current model, stated here rather than
            // implied: when admins become per-site, this is the list that narrows, and the
            // X-Site-Key check in AdminSiteResolutionMiddleware narrows with it.
            var options = _siteData.GetSites()
                .Where(site => site.IsActive)
                .OrderBy(site => site.Name)
                .Select(site => new SiteOption(
                    site.Id, site.SiteKey, site.Name, site.Country, site.CurrencyCode))
                .ToList();

            return Ok(options);
        }
    }
}
