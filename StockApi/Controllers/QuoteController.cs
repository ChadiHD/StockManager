using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Sites;

namespace StockApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class QuoteController : ControllerBase
    {
        private readonly IQuoteData _quoteData;
        private readonly IAdminSiteContext _site;

        public QuoteController(IQuoteData quoteData, IAdminSiteContext site)
        {
            _quoteData = quoteData;
            _site = site;
        }

        [HttpGet]
        public List<QuoteModel> GetAll()
        {
            return _quoteData.GetQuotes(_site.SiteId);
        }

        [HttpGet("{reference}")]
        public ActionResult<QuoteModel> GetByReference(string reference)
        {
            var quote = _quoteData.GetQuoteByReference(reference, _site.SiteId);

            return quote is null ? NotFound() : quote;
        }

        [HttpGet("{reference}/Lines")]
        public ActionResult<List<QuoteLineModel>> GetLines(string reference)
        {
            var quote = _quoteData.GetQuoteByReference(reference, _site.SiteId);

            return quote is null ? NotFound() : _quoteData.GetQuoteLines(quote.Id, _site.SiteId);
        }

        public record NewQuoteModel(int AccountId, string Currency, DateTime? ExpiresDate);

        [HttpPost]
        public ActionResult<QuoteModel> Create(NewQuoteModel quote)
        {
            return _quoteData.CreateQuote(quote.AccountId, quote.Currency, quote.ExpiresDate, _site.SiteId);
        }

        public record NewQuoteLineModel(int ProductId, int Quantity, decimal ListPrice, int DiscountPct);

        [HttpPost("{reference}/Lines")]
        public IActionResult AddLine(string reference, NewQuoteLineModel line)
        {
            var quote = _quoteData.GetQuoteByReference(reference, _site.SiteId);
            if (quote is null)
            {
                return NotFound();
            }

            _quoteData.AddQuoteLine(quote.Id, line.ProductId, line.Quantity, line.ListPrice, line.DiscountPct, _site.SiteId);

            return NoContent();
        }

        // The quote is resolved from its reference and passed down, so the line id alone is not
        // enough to remove a line: a guessed id that belongs to another quote deletes nothing.
        [HttpDelete("{reference}/Lines/{lineId:int}")]
        public IActionResult DeleteLine(string reference, int lineId)
        {
            var quote = _quoteData.GetQuoteByReference(reference, _site.SiteId);
            if (quote is null)
            {
                return NotFound();
            }

            return _quoteData.DeleteQuoteLine(quote.Id, lineId, _site.SiteId) ? NoContent() : NotFound();
        }

        public record QuoteStatusModel(string Status);

        [HttpPut("{reference}/Status")]
        public IActionResult UpdateStatus(string reference, QuoteStatusModel change)
        {
            // CK_Quote_Status refuses anything else, and a constraint violation from inside a
            // procedure reaches the caller as a 500. Refusing it here makes it an answer.
            if (!QuoteStatus.IsKnown(change.Status))
            {
                return BadRequest(new
                {
                    change.Status,
                    Message = $"A quote status is one of: {string.Join(", ", QuoteStatus.All)}."
                });
            }

            var quote = _quoteData.GetQuoteByReference(reference, _site.SiteId);
            if (quote is null)
            {
                return NotFound();
            }

            _quoteData.UpdateStatus(quote.Id, change.Status, _site.SiteId);

            return NoContent();
        }
    }
}
