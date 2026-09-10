using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;

namespace StockApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class QuoteController : ControllerBase
    {
        private readonly IQuoteData _quoteData;

        public QuoteController(IQuoteData quoteData)
        {
            _quoteData = quoteData;
        }

        [HttpGet]
        public List<QuoteModel> GetAll()
        {
            return _quoteData.GetQuotes();
        }

        [HttpGet("{reference}")]
        public ActionResult<QuoteModel> GetByReference(string reference)
        {
            var quote = _quoteData.GetQuoteByReference(reference);

            return quote is null ? NotFound() : quote;
        }

        [HttpGet("{reference}/Lines")]
        public ActionResult<List<QuoteLineModel>> GetLines(string reference)
        {
            var quote = _quoteData.GetQuoteByReference(reference);

            return quote is null ? NotFound() : _quoteData.GetQuoteLines(quote.Id);
        }

        public record NewQuoteModel(int AccountId, string Currency, DateTime? ExpiresDate);

        [HttpPost]
        public ActionResult<QuoteModel> Create(NewQuoteModel quote)
        {
            return _quoteData.CreateQuote(quote.AccountId, quote.Currency, quote.ExpiresDate);
        }

        public record NewQuoteLineModel(int ProductId, int Quantity, decimal ListPrice, int DiscountPct);

        [HttpPost("{reference}/Lines")]
        public IActionResult AddLine(string reference, NewQuoteLineModel line)
        {
            var quote = _quoteData.GetQuoteByReference(reference);
            if (quote is null)
            {
                return NotFound();
            }

            _quoteData.AddQuoteLine(quote.Id, line.ProductId, line.Quantity, line.ListPrice, line.DiscountPct);

            return NoContent();
        }

        // The quote is resolved from its reference and passed down, so the line id alone is not
        // enough to remove a line: a guessed id that belongs to another quote deletes nothing.
        [HttpDelete("{reference}/Lines/{lineId:int}")]
        public IActionResult DeleteLine(string reference, int lineId)
        {
            var quote = _quoteData.GetQuoteByReference(reference);
            if (quote is null)
            {
                return NotFound();
            }

            return _quoteData.DeleteQuoteLine(quote.Id, lineId) ? NoContent() : NotFound();
        }

        public record QuoteStatusModel(string Status);

        [HttpPut("{reference}/Status")]
        public IActionResult UpdateStatus(string reference, QuoteStatusModel change)
        {
            var quote = _quoteData.GetQuoteByReference(reference);
            if (quote is null)
            {
                return NotFound();
            }

            _quoteData.UpdateStatus(quote.Id, change.Status);

            return NoContent();
        }
    }
}
