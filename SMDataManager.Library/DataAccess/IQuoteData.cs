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
        void AddQuoteLine(int quoteId, int productId, int quantity, decimal listPrice, int discountPct, int siteId);

        /// <summary>Removes one line from a quote. False when the line is not on that quote.</summary>
        bool DeleteQuoteLine(int quoteId, int lineId, int siteId);
    }
}
