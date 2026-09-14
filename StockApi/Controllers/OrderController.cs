using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
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

        public record ConvertQuoteModel(string QuoteReference);

        [HttpPost("FromQuote")]
        public ActionResult<OrderModel> CreateFromQuote(ConvertQuoteModel conversion, [FromServices] IQuoteData quoteData)
        {
            var quote = quoteData.GetQuoteByReference(conversion.QuoteReference, _site.SiteId);
            if (quote is null)
            {
                return NotFound();
            }

            string staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            return _orderData.ConvertQuoteToOrder(quote.Id, staffId, _site.SiteId);
        }

        public record OrderStatusModel(string Status);

        [HttpPut("{reference}/Status")]
        public IActionResult UpdateStatus(string reference, OrderStatusModel change)
        {
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
