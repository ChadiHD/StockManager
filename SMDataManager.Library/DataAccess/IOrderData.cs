using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IOrderData
    {
        List<OrderModel> GetOrders();
        OrderModel GetOrderByReference(string reference);
        List<OrderLineModel> GetOrderLines(int purchaseId);
        OrderModel CreateOrder(string staffId, int accountId, string currency);
        OrderModel ConvertQuoteToOrder(int quoteId, string staffId);
        void UpdateStatus(int purchaseId, string status);
        List<SalesReportModel> GetSalesReport();
        List<ActivityModel> GetRecentActivity(int take);
    }
}
