using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// Quotes and their lines, scoped to a store. See <see cref="IAccountData"/> for why
    /// siteId is mandatory and last on every method.
    /// </summary>
    /// <remarks>
    /// A quote reference is unique across every store, so the site is purely a security
    /// predicate here: it stops one store's admin opening another's quote by guessing
    /// QT-0041. Lines carry no site of their own and are gated through their quote.
    /// </remarks>
    public interface IQuoteData
    {
        List<QuoteModel> GetQuotes(int siteId);
        QuoteModel GetQuoteByReference(string reference, int siteId);
        List<QuoteLineModel> GetQuoteLines(int quoteId, int siteId);

        /// <summary>One account's quotes, for the customer's own list.</summary>
        /// <remarks>
        /// Separate from <see cref="GetQuotes"/> because the predicate differs in kind: an
        /// admin may see every quote in their store, a customer only their own. See the
        /// remarks on the procedures for why a reference is not an authorisation.
        /// </remarks>
        List<QuoteModel> GetQuotesForAccount(int accountId, int siteId);

        QuoteModel GetQuoteForAccount(string reference, int accountId, int siteId);

        List<QuoteLineModel> GetQuoteLinesForAccount(int quoteId, int accountId, int siteId);

        /// <summary>
        /// The customer turning a quote down. False when somebody already decided it.
        /// </summary>
        bool RejectForAccount(int quoteId, int accountId, int siteId, string reason);
        QuoteModel CreateQuote(int accountId, string currency, DateTime? expiresDate, int siteId);

        /// <summary>
        /// Turns a basket into a Requested quote, and empties the basket, in one transaction.
        /// </summary>
        /// <remarks>
        /// The reference is returned rather than the caller re-querying for the newest row:
        /// two customers submitting at once would both read the other's.
        /// </remarks>
        QuoteModel SubmitRequest(QuoteRequest request);
        void UpdateStatus(int quoteId, string status, int siteId);
        /// <summary>Adds a line, at a stated net price or at one derived from the discount.</summary>
        /// <remarks>
        /// <paramref name="netPrice"/> is how the storefront records the price the customer was
        /// actually shown. Left null, the procedure derives it, which is what the admin portal
        /// wants when somebody types a list price and a discount. See spQuoteLine_Insert.
        /// </remarks>
        void AddQuoteLine(int quoteId, int productId, int quantity, decimal listPrice,
            decimal discountPct, int siteId, decimal? netPrice = null);

        /// <summary>
        /// Re-prices or re-quantifies one line. False when the line is not on that quote, or
        /// when the quote has already been accepted and its lines are an order's record.
        /// </summary>
        /// <remarks>
        /// <paramref name="netPrice"/> follows the same rule as
        /// <see cref="AddQuoteLine"/>: stated, it is stored; left null, the procedure derives
        /// it from the line's own list price and the discount.
        /// </remarks>
        bool UpdateQuoteLine(int quoteId, int lineId, int quantity, decimal discountPct,
            int siteId, decimal? netPrice = null);

        /// <summary>Removes one line from a quote. False when the line is not on that quote.</summary>
        bool DeleteQuoteLine(int quoteId, int lineId, int siteId);

        /// <summary>
        /// Sends a priced quote to the customer, making it decidable. False when they already
        /// accepted or rejected it.
        /// </summary>
        /// <remarks>
        /// A claim, not a status write: see spQuote_Price for why this is not
        /// <see cref="UpdateStatus"/> with a string.
        /// </remarks>
        bool Price(int quoteId, int siteId);
    }
}
