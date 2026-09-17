using System.Collections.Generic;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// A basket on its way to becoming a quote, with a price resolved per line.
    /// </summary>
    /// <remarks>
    /// The prices are the caller's, deliberately. They come from <c>PriceResolver</c> through
    /// <c>CatalogPresenter</c>, which is the same path that rendered them, so the quote records
    /// what the customer was looking at when they pressed submit. They are re-resolved
    /// server-side at that moment rather than posted by the browser — a price in a form is a
    /// client's opinion about what things cost.
    ///
    /// The account is absent on purpose: <c>spQuote_SubmitRequest</c> derives it from the
    /// contact, because a session proves a contact and nothing else.
    /// </remarks>
    public class QuoteRequest
    {
        public int ContactId { get; set; }
        public int SiteId { get; set; }

        /// <summary>Whatever the customer wrote alongside the request. Never markup.</summary>
        public string CustomerNote { get; set; }

        /// <summary>
        /// The basket to empty on success, or null. Deleted inside the same transaction, so a
        /// basket cannot outlive the submit it produced and invite a second one.
        /// </summary>
        public int? BasketId { get; set; }

        public List<QuoteRequestLine> Lines { get; set; } = new List<QuoteRequestLine>();
    }

    /// <summary>One line of a quote request, priced.</summary>
    public class QuoteRequestLine
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal ListPrice { get; set; }
        public decimal DiscountPct { get; set; }
        public decimal NetPrice { get; set; }
    }
}
