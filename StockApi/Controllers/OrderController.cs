using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Tax;
using StockApi.Sites;
using System.Security.Claims;

namespace StockApi.Controllers
{
    // Sales orders. These are dbo.Purchase rows carrying a Reference; POS sales written by the
    // desktop app have a NULL Reference and are not returned here.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderData _orderData;
        private readonly IAdminSiteContext _site;

        public OrderController(IOrderData orderData, IAdminSiteContext site)
        {
            _orderData = orderData;
            _site = site;
        }

        [HttpGet]
        public List<OrderModel> GetAll()
        {
            return _orderData.GetOrders(_site.SiteId);
        }

        [HttpGet("{reference}")]
        public ActionResult<OrderModel> GetByReference(string reference)
        {
            var order = _orderData.GetOrderByReference(reference, _site.SiteId);

            return order is null ? NotFound() : order;
        }

        [HttpGet("{reference}/Lines")]
        public ActionResult<List<OrderLineModel>> GetLines(string reference)
        {
            var order = _orderData.GetOrderByReference(reference, _site.SiteId);

            return order is null ? NotFound() : _orderData.GetOrderLines(order.Id, _site.SiteId);
        }

        public record NewOrderModel(int AccountId, string Currency);

        [HttpPost]
        public ActionResult<OrderModel> Create(NewOrderModel order)
        {
            // Taken from the token rather than the request body so a caller cannot attribute an
            // order to another member of staff.
            string staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            return _orderData.CreateOrder(staffId, order.AccountId, order.Currency, _site.SiteId);
        }

        public record ConvertQuoteModel(string QuoteReference, string? PoNumber = null);

        [HttpPost("FromQuote")]
        public ActionResult<OrderModel> CreateFromQuote(
            ConvertQuoteModel conversion,
            [FromServices] IQuoteData quoteData,
            [FromServices] IAccountData accountData,
            [FromServices] TaxAssessor taxAssessor)
        {
            var quote = quoteData.GetQuoteByReference(conversion.QuoteReference, _site.SiteId);
            if (quote is null)
            {
                return NotFound();
            }

            // Taken from the token rather than the request body so a conversion cannot be
            // attributed to another member of staff. StaffId is a foreign key into dbo.[User],
            // so a claim naming nobody fails in the database rather than at the boundary --
            // which is how Account.ApprovedBy shipped broken in T3.
            string staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Assessed here rather than inside the procedure: reverse charge turns on the
            // customer's country and VAT number against this store's, and the same assessor
            // runs on the customer's own accept path so the two cannot reach different
            // answers about one sale.
            var assessment = taxAssessor.ForOrder(
                _site.Site, accountData.GetAccountById(quote.AccountId, _site.SiteId));

            var outcome = _orderData.ConvertQuoteToOrder(
                quote.Id, QuoteAcceptance.ByStaff(staffId, conversion.PoNumber), _site.SiteId,
                assessment);

            // Somebody else accepted or rejected it while this screen was open. Reporting
            // success would show a conversion this request did not make -- the same reasoning
            // as spAccount_Approve's no-op answering Conflict.
            if (outcome.NoLongerAwaitingAcceptance)
            {
                return Conflict(new { conversion.QuoteReference, Message = "That quote is no longer awaiting acceptance." });
            }

            return outcome.Order;
        }

        public record OrderStatusModel(string Status);

        [HttpPut("{reference}/Status")]
        public IActionResult UpdateStatus(string reference, OrderStatusModel change)
        {
            // CK_Purchase_Status refuses anything else, and a constraint violation raised
            // inside a procedure reaches the caller as a 500. Refusing it here makes it an
            // answer — the same guard QuoteController gained in T5, one table over.
            if (!OrderStatus.IsKnown(change.Status))
            {
                return BadRequest(new
                {
                    change.Status,
                    Message = $"An order status is one of: {string.Join(", ", OrderStatus.All)}."
                });
            }

            var order = _orderData.GetOrderByReference(reference, _site.SiteId);
            if (order is null)
            {
                return NotFound();
            }

            _orderData.UpdateStatus(order.Id, change.Status, _site.SiteId);

            return NoContent();
        }

        [HttpGet("Report")]
        public List<SalesReportModel> GetReport()
        {
            return _orderData.GetSalesReport(_site.SiteId);
        }

        [HttpGet("Activity")]
        public List<ActivityModel> GetActivity(int take = 10)
        {
            return _orderData.GetRecentActivity(take, _site.SiteId);
        }
    }
}
