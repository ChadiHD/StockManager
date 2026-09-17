using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using StockApi.Sites;
using StockManager.Documents;

namespace StockApi.Controllers
{
    /// <summary>
    /// The reviewer's side of customer document upload: list what an applicant sent, read it,
    /// and mark each one accepted or rejected.
    /// </summary>
    /// <remarks>
    /// Documents are the reason approval is a human step. Nothing here decides anything — a
    /// per-document verdict is recorded so a single bad scan does not have to fail a whole
    /// application, and the account transition is still <c>spAccount_Approve</c>.
    /// </remarks>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AccountDocumentController : ControllerBase
    {
        private readonly IAccountDocumentData _documents;
        private readonly IAccountData _accounts;
        private readonly IDocumentStore _store;
        private readonly IAdminSiteContext _site;

        public AccountDocumentController(
            IAccountDocumentData documents,
            IAccountData accounts,
            IDocumentStore store,
            IAdminSiteContext site)
        {
            _documents = documents;
            _accounts = accounts;
            _store = store;
            _site = site;
        }

        /// <summary>What an account sent in, without the means to fetch any of it.</summary>
        public record DocumentListItem(
            int Id,
            int AccountId,
            string Kind,
            string OriginalName,
            string ContentType,
            long SizeBytes,
            DateTime UploadedUtc,
            string Status);

        /*
        A projection rather than the model, and that is the whole point of it.

        AccountDocumentModel carries StoredName, and spAccountDocument_GetById fills it in.
        Returning the model from any endpoint would publish the key into the document store
        to every admin client and, from there, to anything that logged a response body. The
        list procedure does not even select it; this makes sure the detail path cannot leak
        it either.
        */
        [HttpGet]
        public ActionResult<List<DocumentListItem>> GetByAccount([FromQuery] int accountId)
        {
            if (_accounts.GetAccountById(accountId, _site.SiteId) is null)
            {
                return NotFound();
            }

            return _documents.GetByAccount(accountId, _site.SiteId)
                .Select(document => new DocumentListItem(
                    document.Id, document.AccountId, document.Kind, document.OriginalName,
                    document.ContentType, document.SizeBytes, document.UploadedUtc, document.Status))
                .ToList();
        }

        /// <summary>
        /// Streams one document back to the reviewer.
        /// </summary>
        /// <remarks>
        /// 404 for a document belonging to another store, because the query that resolves it
        /// carries the site predicate and returns nothing. That is the same answer the
        /// storefront gives a customer asking for someone else's, and for the same reason: a
        /// 403 confirms the row exists.
        /// </remarks>
        [HttpGet("{id:int}/Content")]
        public async Task<IActionResult> GetContent(int id, CancellationToken cancellationToken)
        {
            var document = _documents.GetById(id, _site.SiteId);

            if (document is null)
            {
                return NotFound();
            }

            var content = await _store.OpenAsync(_site.Site.SiteKey, document.StoredName, cancellationToken);

            if (content is null)
            {
                return NotFound();
            }

            Response.Headers.XContentTypeOptions = "nosniff";

            // As an attachment, always. These are files a stranger uploaded, and the admin
            // origin holds the session that can approve accounts — rendering one inline is
            // the one place that would matter most.
            return File(content, document.ContentType, DownloadName(document.OriginalName));
        }

        public record DocumentStatusChange(string Status);

        [HttpPut("{id:int}/Status")]
        public IActionResult SetStatus(int id, DocumentStatusChange change)
        {
            if (change.Status is not ("Pending" or "Accepted" or "Rejected"))
            {
                return BadRequest("Status must be Pending, Accepted or Rejected.");
            }

            if (_documents.GetById(id, _site.SiteId) is null)
            {
                return NotFound();
            }

            _documents.SetStatus(id, change.Status, _site.SiteId);

            return NoContent();
        }

        /// <summary>Strips what a filename must not carry into a header, and nothing else.</summary>
        private static string DownloadName(string original)
        {
            if (string.IsNullOrWhiteSpace(original))
            {
                return "document";
            }

            var cleaned = new string(original
                .Where(character => !char.IsControl(character) && character != '/' && character != '\\')
                .ToArray())
                .Trim();

            return cleaned.Length == 0 ? "document" : cleaned;
        }
    }
}
