using Microsoft.Data.SqlClient;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class OrderData : IOrderData
    {
        /// <summary>The number spOrder_ConvertFromQuote THROWs when the claim is refused.</summary>
        private const int QuoteAlreadyDecided = 50010;

        private readonly ISqlDataAccess _sqlDataAccess;

        public OrderData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<OrderModel> GetOrders(int siteId)
        {
            return _sqlDataAccess.LoadData<OrderModel, dynamic>(
                "dbo.spOrder_GetAll", new { SiteId = siteId }, "SMDatabase");
        }

        public OrderModel GetOrderByReference(string reference, int siteId)
        {
            return _sqlDataAccess.LoadData<OrderModel, dynamic>(
                "dbo.spOrder_GetByReference", new { Reference = reference, SiteId = siteId },
                "SMDatabase").FirstOrDefault();
        }

        public List<OrderLineModel> GetOrderLines(int purchaseId, int siteId)
        {
            return _sqlDataAccess.LoadData<OrderLineModel, dynamic>(
                "dbo.spOrderLine_GetByOrder", new { PurchaseId = purchaseId, SiteId = siteId }, "SMDatabase");
        }

        public OrderModel CreateOrder(string staffId, int accountId, string currency, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spOrder_Insert", new
            {
                Id = 0,
                Reference = string.Empty,
                StaffId = staffId,
                AccountId = accountId,
                Currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency,
                QuoteId = (int?)null,
                SiteId = siteId
            }, "SMDatabase");

            return GetOrders(siteId)
                .Where(x => x.AccountId == accountId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();
        }

        public OrderModel GetOrderByQuote(int quoteId, int siteId)
        {
            return _sqlDataAccess.LoadData<OrderModel, dynamic>(
                "dbo.spOrder_GetByQuote", new { QuoteId = quoteId, SiteId = siteId },
                "SMDatabase").FirstOrDefault();
        }

        public QuoteAcceptanceResult ConvertQuoteToOrder(
            int quoteId, QuoteAcceptance acceptance, int siteId)
        {
            try
            {
                _sqlDataAccess.SaveData("dbo.spOrder_ConvertFromQuote", new
                {
                    QuoteId = quoteId,
                    Id = 0,
                    Reference = string.Empty,
                    SiteId = siteId,
                    acceptance.StaffId,
                    acceptance.PlacedByContactId,
                    acceptance.PoNumber
                }, "SMDatabase");
            }
            catch (SqlException ex) when (ex.Number == QuoteAlreadyDecided)
            {
                // Not a fault in the request: somebody else decided the quote while this
                // screen was open, which under T5 is the ordinary race between an admin's
                // Convert and a customer's Accept. The procedure created nothing.
                return QuoteAcceptanceResult.AlreadyDecided();
            }

            // By the quote, not by the store's newest order. UQ_Purchase_QuoteId makes one
            // order per quote a database fact, so this reads back what this call created;
            // "newest" would be another caller's order under two concurrent conversions.
            return QuoteAcceptanceResult.Converted(GetOrderByQuote(quoteId, siteId));
        }

        public void UpdateStatus(int purchaseId, string status, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spOrder_UpdateStatus",
                new { Id = purchaseId, Status = status, SiteId = siteId }, "SMDatabase");
        }

        public List<SalesReportModel> GetSalesReport(int siteId)
        {
            return _sqlDataAccess.LoadData<SalesReportModel, dynamic>(
                "dbo.spReport_GetSales", new { SiteId = siteId }, "SMDatabase");
        }

        public List<ActivityModel> GetRecentActivity(int take, int siteId)
        {
            return _sqlDataAccess.LoadData<ActivityModel, dynamic>(
                "dbo.spActivity_GetRecent", new { Take = take, SiteId = siteId }, "SMDatabase");
        }
    }
}
