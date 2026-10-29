using SMDataManager.Library.Models;
using SMDataManager.Library.Tax;
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

        /// <summary>One account's orders, for the customer's own list.</summary>
        /// <remarks>See <see cref="IQuoteData.GetQuotesForAccount"/>.</remarks>
        List<OrderModel> GetOrdersForAccount(int accountId, int siteId);

        OrderModel GetOrderForAccount(string reference, int accountId, int siteId);

        List<OrderLineModel> GetOrderLinesForAccount(int purchaseId, int accountId, int siteId);
        /// <summary>An order in the store's own currency (T9): it is not the caller's to choose.</summary>
        OrderModel CreateOrder(string staffId, int accountId, int siteId);
        OrderModel GetOrderByQuote(int quoteId, int siteId);

        /// <summary>
        /// Turns a quote into an order, or reports that somebody else already decided it.
        /// </summary>
        /// <remarks>
        /// The procedure claims the quote's status transition, so a second caller creates
        /// nothing and gets <c>NoLongerAwaitingAcceptance</c> rather than a duplicate order.
        /// </remarks>
        QuoteAcceptanceResult ConvertQuoteToOrder(
            int quoteId, QuoteAcceptance acceptance, int siteId, TaxAssessment assessment = null,
            bool enforceCreditLimit = false);
        /// <summary>
        /// False when the store refused: a cancelled order from the cart cannot be reopened,
        /// because its stock has gone back (T12).
        /// </summary>
        bool UpdateStatus(int purchaseId, string status, int siteId);

        /// <summary>
        /// Turns a cart into an order at <c>Awaiting payment</c>, taking its own stock (T12).
        /// </summary>
        /// <remarks>
        /// A short order writes nothing and names the SKUs, rather than throwing.
        /// </remarks>
        OrderPlacementResult PlaceFromBasket(OrderPlacement placement);

        /// <summary>A guest's order, by the token in its link. The token is the authorisation.</summary>
        OrderModel GetOrderByGuestToken(string token, int siteId);

        /// <summary>Where an order goes, or null for an order with no address.</summary>
        OrderAddressModel GetDeliveryAddress(int purchaseId, int siteId);
        List<SalesReportModel> GetSalesReport(int siteId);
        List<ActivityModel> GetRecentActivity(int take, int siteId);
    }
}
