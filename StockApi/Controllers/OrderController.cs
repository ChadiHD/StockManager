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

        /// <summary>
        /// Where an order goes (T12): the address its checkout gave. An order from a quote has none,
        /// and that is a 404 like an order that is not here.
        /// </summary>
        [HttpGet("{reference}/Delivery")]
        public ActionResult<OrderAddressModel> GetDelivery(string reference)
        {
            var order = _orderData.GetOrderByReference(reference, _site.SiteId);
            var address = order is null ? null : _orderData.GetDeliveryAddress(order.Id, _site.SiteId);

            return address is null ? NotFound() : address;
        }

        [HttpGet("{reference}/Lines")]
        public ActionResult<List<OrderLineModel>> GetLines(string reference)
        {
            var order = _orderData.GetOrderByReference(reference, _site.SiteId);

            return order is null ? NotFound() : _orderData.GetOrderLines(order.Id, _site.SiteId);
        }

        // No currency: an order is in its store's (T9).
        public record NewOrderModel(int AccountId);

        [HttpPost]
        public ActionResult<OrderModel> Create(NewOrderModel order)
        {
            // Taken from the token rather than the request body so a caller cannot attribute an
            // order to another member of staff.
            string staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            return _orderData.CreateOrder(staffId, order.AccountId, _site.SiteId);
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

        /// <summary>A status, and for a cart order marked fulfilled its tracking number (T13).</summary>
        public record OrderStatusModel(string Status, string? TrackingNumber = null);

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

            if (change.TrackingNumber?.Trim().Length > 100)
            {
                return BadRequest(new { Message = "A tracking number is up to 100 characters." });
            }

            if (!_orderData.UpdateStatus(order.Id, change.Status, _site.SiteId, change.TrackingNumber))
            {
                // A cancelled cart order has given its stock back (T12), and reopening it would take
                // the stock again unchecked. An answer the admin can act on, not a failure.
                return Conflict(new { Message = "A cancelled order placed from the cart cannot be reopened. Its stock has been returned." });
            }

            return NoContent();
        }

        /// <summary>
        /// Sends a dead-lettered fiscal document again (T14), once whatever the issuer refused has
        /// been fixed. A 409 when it was not dead-lettered: somebody already did, or it was issued.
        /// </summary>
        [HttpPost("{reference}/FiscalDocument/Retry")]
        public IActionResult RetryFiscalDocument(string reference, [FromServices] IFiscalDocumentData documents)
        {
            return documents.Retry(_site.SiteId, reference)
                ? NoContent()
                : Conflict(new { Message = "That order has no failed fiscal document to retry." });
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
