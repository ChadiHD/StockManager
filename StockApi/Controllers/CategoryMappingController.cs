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

        public record CategoryModel(string? Slug, string? Name, string? Blurb, int SortOrder, bool IsActive);

        // The store's own categories (T9): what the feed categories above file under.

        [HttpPost("Categories")]
        public IActionResult CreateCategory(CategoryModel category) => Save(0, category);

        [HttpPut("Categories/{id:int}")]
        public IActionResult UpdateCategory(int id, CategoryModel category) => Save(id, category);

        private IActionResult Save(int id, CategoryModel category)
        {
            var slug = category.Slug?.Trim().ToLowerInvariant() ?? string.Empty;

            // The slug is a query-string value on every catalog link, so it is held to what reads
            // cleanly in a URL and needs no escaping.
            if (!CategorySlug.IsMatch(slug))
            {
                return BadRequest("The address is lower-case letters, digits and single hyphens, up to 80 characters — such as networking-kit.");
            }

            if (string.IsNullOrWhiteSpace(category.Name) || category.Name.Trim().Length > 120)
            {
                return BadRequest("A category needs a name of up to 120 characters.");
            }

            if ((category.Blurb?.Trim().Length ?? 0) > 400)
            {
                return BadRequest("The blurb is at most 400 characters.");
            }

            var (savedId, refusal) = _mappings.SaveCategory(_site.SiteId, new SiteCategoryModel
            {
                Id = id,
                Slug = slug,
                Name = category.Name.Trim(),
                Blurb = category.Blurb,
                SortOrder = category.SortOrder,
                IsActive = category.IsActive
            });

            return refusal is null ? Ok(new { id = savedId }) : BadRequest(refusal);
        }

        private static readonly System.Text.RegularExpressions.Regex CategorySlug =
            new(@"^(?=.{1,80}$)[a-z0-9]+(-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.Compiled);
    }
}
