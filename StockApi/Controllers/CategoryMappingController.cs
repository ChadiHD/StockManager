using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Sites;

namespace StockApi.Controllers
{
    /// <summary>
    /// Which feed categories the acting store sells, and under which of its own categories.
    /// </summary>
    /// <remarks>
    /// The bulk lever over a store's catalog: mapping a feed category puts every product in it
    /// on sale, including the ones tomorrow's feed adds. Before T8 this was only reachable by
    /// writing rows, and the portal's product form offered a hardcoded list of seven categories
    /// that rewrote a product's feed category instead.
    /// </remarks>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class CategoryMappingController : ControllerBase
    {
        private readonly ICategoryMappingData _mappings;
        private readonly IAdminSiteContext _site;

        public CategoryMappingController(ICategoryMappingData mappings, IAdminSiteContext site)
        {
            _mappings = mappings;
            _site = site;
        }

        public record CategoryMappingView(
            IReadOnlyList<SiteCategoryModel> Categories, IReadOnlyList<FeedCategoryModel> FeedCategories);

        [HttpGet]
        public CategoryMappingView Get() => new(
            _mappings.GetSiteCategories(_site.SiteId),
            _mappings.GetFeedCategories(_site.SiteId));

        /// <summary>A null category stops the store selling that feed category.</summary>
        /// <remarks>
        /// The feed value travels in the body, not the route: feed categories are the
        /// distributor's strings, and "Keyboards / Desktops" is one of them.
        /// </remarks>
        public record MappingModel(string FeedValue, int? SiteCategoryId);

        [HttpPut]
        public IActionResult Put(MappingModel mapping)
        {
            if (string.IsNullOrWhiteSpace(mapping.FeedValue))
            {
                return BadRequest("A feed category is required.");
            }

            var refusal = _mappings.Map(_site.SiteId, mapping.FeedValue, mapping.SiteCategoryId);

            return refusal is null ? NoContent() : BadRequest(refusal);
        }
    }
}
