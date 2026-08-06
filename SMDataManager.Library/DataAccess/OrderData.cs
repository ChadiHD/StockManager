using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class OrderData : IOrderData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public OrderData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<OrderModel> GetOrders()
        {
            return _sqlDataAccess.LoadData<OrderModel, dynamic>(
                "dbo.spOrder_GetAll", new { }, "SMDatabase");
        }

        public OrderModel GetOrderByReference(string reference)
        {
            return _sqlDataAccess.LoadData<OrderModel, dynamic>(
                "dbo.spOrder_GetByReference", new { Reference = reference }, "SMDatabase").FirstOrDefault();
        }

        public List<OrderLineModel> GetOrderLines(int purchaseId)
        {
            return _sqlDataAccess.LoadData<OrderLineModel, dynamic>(
                "dbo.spOrderLine_GetByOrder", new { PurchaseId = purchaseId }, "SMDatabase");
        }

        public OrderModel CreateOrder(string staffId, int accountId, string currency)
        {
            _sqlDataAccess.SaveData("dbo.spOrder_Insert", new
            {
                Id = 0,
                Reference = string.Empty,
                StaffId = staffId,
                AccountId = accountId,
                Currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency,
                QuoteId = (int?)null
            }, "SMDatabase");

            return GetOrders()
                .Where(x => x.AccountId == accountId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();
        }

        public OrderModel ConvertQuoteToOrder(int quoteId, string staffId)
        {
            _sqlDataAccess.SaveData("dbo.spOrder_ConvertFromQuote", new
            {
                QuoteId = quoteId,
                StaffId = staffId,
                Id = 0,
                Reference = string.Empty
            }, "SMDatabase");

            return GetOrders()
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();
        }

        public void UpdateStatus(int purchaseId, string status)
        {
            _sqlDataAccess.SaveData("dbo.spOrder_UpdateStatus",
                new { Id = purchaseId, Status = status }, "SMDatabase");
        }

        public List<SalesReportModel> GetSalesReport()
        {
            return _sqlDataAccess.LoadData<SalesReportModel, dynamic>(
                "dbo.spReport_GetSales", new { }, "SMDatabase");
        }

        public List<ActivityModel> GetRecentActivity(int take)
        {
            return _sqlDataAccess.LoadData<ActivityModel, dynamic>(
                "dbo.spActivity_GetRecent", new { Take = take }, "SMDatabase");
        }
    }
}
