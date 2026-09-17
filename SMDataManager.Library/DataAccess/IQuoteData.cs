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
        QuoteModel CreateQuote(int accountId, string currency, DateTime? expiresDate, int siteId);
        void UpdateStatus(int quoteId, string status, int siteId);
        /// <summary>Adds a line, at a stated net price or at one derived from the discount.</summary>
        /// <remarks>
        /// <paramref name="netPrice"/> is how the storefront records the price the customer was
        /// actually shown. Left null, the procedure derives it, which is what the admin portal
        /// wants when somebody types a list price and a discount. See spQuoteLine_Insert.
        /// </remarks>
        void AddQuoteLine(int quoteId, int productId, int quantity, decimal listPrice,
            decimal discountPct, int siteId, decimal? netPrice = null);

        /// <summary>Removes one line from a quote. False when the line is not on that quote.</summary>
        bool DeleteQuoteLine(int quoteId, int lineId, int siteId);
    }
}
