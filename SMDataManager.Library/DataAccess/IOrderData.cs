using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// Portal sales orders and the reports over them, scoped to a store. See
    /// <see cref="IAccountData"/> for why siteId is mandatory and last on every method.
    /// </summary>
    /// <remarks>
    /// An order is a dbo.Purchase row carrying a Reference. POS sales share the table, have no
    /// site, and are excluded by the Reference predicate rather than by the site one — the two
    /// filters are independent and both are needed.
    /// </remarks>
    public interface IOrderData
    {
        List<OrderModel> GetOrders(int siteId);
        OrderModel GetOrderByReference(string reference, int siteId);
        List<OrderLineModel> GetOrderLines(int purchaseId, int siteId);
        OrderModel CreateOrder(string staffId, int accountId, string currency, int siteId);
        OrderModel GetOrderByQuote(int quoteId, int siteId);

        /// <summary>
        /// Turns a quote into an order, or reports that somebody else already decided it.
        /// </summary>
        /// <remarks>
        /// The procedure claims the quote's status transition, so a second caller creates
        /// nothing and gets <c>NoLongerAwaitingAcceptance</c> rather than a duplicate order.
        /// </remarks>
        QuoteAcceptanceResult ConvertQuoteToOrder(int quoteId, QuoteAcceptance acceptance, int siteId);
        void UpdateStatus(int purchaseId, string status, int siteId);
        List<SalesReportModel> GetSalesReport(int siteId);
        List<ActivityModel> GetRecentActivity(int take, int siteId);
    }
}
