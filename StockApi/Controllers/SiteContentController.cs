using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Content;
using StockApi.Sites;

namespace StockApi.Controllers
{
    /// <summary>
    /// The acting store's content pages — home, about, terms and the rest (T9). Until this
    /// existed, dbo.SiteContent was written by hand.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class SiteContentController : ControllerBase
    {
        private const int MaxBodyLength = 200_000;

        private readonly ISiteContentData _content;
        private readonly IAdminSiteContext _site;

        public SiteContentController(ISiteContentData content, IAdminSiteContext site)
        {
            _content = content;
            _site = site;
        }

        /// <summary>One content page, written or not. <c>Written</c> false means the storefront shows its empty state.</summary>
        public record ContentPageView(string Key, string Label, bool Written, string? Title, string? Lede, string? BodyHtml, DateTime? LastModified);

        [HttpGet]
        public IEnumerable<ContentPageView> Get()
        {
            var written = _content.GetForSite(_site.SiteId, _site.Site.Locale)
                .ToDictionary(page => page.ContentKey);

            return SiteContentKeys.All.Select(entry => written.TryGetValue(entry.Key, out var page)
                ? new ContentPageView(entry.Key, entry.Label, true, page.Title, page.Lede, page.BodyHtml, page.LastModified)
                : new ContentPageView(entry.Key, entry.Label, false, null, null, null, null));
        }

        public record ContentPageEdit(string? Title, string? Lede, string? BodyHtml);

        [HttpPut("{key}")]
        public ActionResult<ContentPageView> Put(string key, ContentPageEdit edit)
        {
            var label = SiteContentKeys.All.FirstOrDefault(entry => entry.Key == key).Label;
            if (label is null) return NotFound();

            if (string.IsNullOrWhiteSpace(edit.Title) || edit.Title.Trim().Length > 200)
                return BadRequest("A page needs a title of up to 200 characters.");

            if ((edit.Lede?.Length ?? 0) > 1000)
                return BadRequest("The standfirst is at most 1,000 characters.");

            if ((edit.BodyHtml?.Length ?? 0) > MaxBodyLength)
                return BadRequest("The page is too long to save in one piece.");

            try
            {
                var saved = _content.Save(_site.SiteId, key, _site.Site.Locale,
                    edit.Title.Trim(), edit.Lede, ContentHtml.Sanitize(edit.BodyHtml));

                // The stored body, cleaned, so the editor shows exactly what customers will read.
                return new ContentPageView(key, label, true, saved.Title, saved.Lede, saved.BodyHtml, saved.LastModified);
            }
            catch (SqlException ex) when (ex.Number == 50110)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
