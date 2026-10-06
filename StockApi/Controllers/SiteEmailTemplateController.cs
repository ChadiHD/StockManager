using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Email;
using SMDataManager.Library.Models;
using StockApi.Sites;

namespace StockApi.Controllers
{
    /// <summary>
    /// The acting store's own wording for each message it sends (T9). dbo.SiteEmailTemplate was
    /// written by hand until this existed, and a mistake in it surfaced only as a warning in the
    /// dispatcher's log while customers got the platform's words.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class SiteEmailTemplateController : ControllerBase
    {
        private readonly ISiteEmailTemplateData _wording;
        private readonly IAdminSiteContext _site;

        public SiteEmailTemplateController(ISiteEmailTemplateData wording, IAdminSiteContext site)
        {
            _wording = wording;
            _site = site;
        }

        /// <summary>
        /// One message: the platform's words, the placeholders it can fill and must keep, and the
        /// store's own words when it has any.
        /// </summary>
        public record EmailWordingView(
            string Key,
            string Audience,
            string PlatformSubject,
            string PlatformBody,
            IReadOnlyList<string> Placeholders,
            IReadOnlyCollection<string> Required,
            bool Customised,
            string? Subject,
            string? Body);

        [HttpGet]
        public IEnumerable<EmailWordingView> Get() =>
            EmailTemplates.All.Select(template => View(template, _wording.Get(_site.SiteId, template.Key)));

        public record EmailWordingEdit(string? Subject, string? Body);

        [HttpPut("{key}")]
        public ActionResult<EmailWordingView> Put(string key, EmailWordingEdit edit)
        {
            var template = EmailTemplates.Find(key);
            if (template is null) return NotFound();

            if ((edit.Subject?.Trim().Length ?? 0) > 200)
                return BadRequest("The subject is at most 200 characters.");

            // Both halves blank is the platform's wording; a row saying so would be noise.
            if (string.IsNullOrWhiteSpace(edit.Subject) && string.IsNullOrWhiteSpace(edit.Body))
            {
                _wording.Delete(_site.SiteId, key);
                return View(template, null);
            }

            var wording = new SiteEmailTemplateModel
            {
                SiteId = _site.SiteId, TemplateKey = key, Subject = edit.Subject?.Trim(), Body = edit.Body?.Trim()
            };

            // The dispatcher's own check, run at the moment somebody can still fix it.
            var refused = EmailRenderer.WhyRefused(template, _site.Site, wording);
            if (refused is not null)
            {
                return BadRequest($"This wording cannot be used: {refused}.");
            }

            _wording.Save(_site.SiteId, key, wording.Subject, wording.Body);

            return View(template, wording);
        }

        [HttpDelete("{key}")]
        public ActionResult<EmailWordingView> Delete(string key)
        {
            var template = EmailTemplates.Find(key);
            if (template is null) return NotFound();

            _wording.Delete(_site.SiteId, key);

            return View(template, null);
        }

        private EmailWordingView View(EmailTemplate template, SiteEmailTemplateModel? wording) => new(
            template.Key,
            template.Audience.ToString(),
            template.DefaultSubject,
            template.DefaultBody,
            EmailRenderer.ValuesFor(template, _site.Site, null).Keys.OrderBy(name => name).ToList(),
            template.RequiredTokens,
            wording is not null,
            wording?.Subject,
            wording?.Body);
    }
}
