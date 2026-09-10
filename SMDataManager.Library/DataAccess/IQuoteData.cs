using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IQuoteData
    {
        List<QuoteModel> GetQuotes();
        QuoteModel GetQuoteByReference(string reference);
        List<QuoteLineModel> GetQuoteLines(int quoteId);
        QuoteModel CreateQuote(int accountId, string currency, DateTime? expiresDate);
        void UpdateStatus(int quoteId, string status);
        void AddQuoteLine(int quoteId, int productId, int quantity, decimal listPrice, int discountPct);

        /// <summary>Removes one line from a quote. False when the line is not on that quote.</summary>
        bool DeleteQuoteLine(int quoteId, int lineId);
    }
}
