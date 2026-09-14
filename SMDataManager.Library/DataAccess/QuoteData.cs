using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class QuoteData : IQuoteData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public QuoteData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<QuoteModel> GetQuotes(int siteId)
        {
            return _sqlDataAccess.LoadData<QuoteModel, dynamic>(
                "dbo.spQuote_GetAll", new { SiteId = siteId }, "SMDatabase");
        }

        public QuoteModel GetQuoteByReference(string reference, int siteId)
        {
            return _sqlDataAccess.LoadData<QuoteModel, dynamic>(
                "dbo.spQuote_GetByReference", new { Reference = reference, SiteId = siteId },
                "SMDatabase").FirstOrDefault();
        }

        public List<QuoteLineModel> GetQuoteLines(int quoteId, int siteId)
        {
            return _sqlDataAccess.LoadData<QuoteLineModel, dynamic>(
                "dbo.spQuoteLine_GetByQuote", new { QuoteId = quoteId, SiteId = siteId }, "SMDatabase");
        }

        public QuoteModel CreateQuote(int accountId, string currency, DateTime? expiresDate, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spQuote_Insert", new
            {
                Id = 0,
                Reference = string.Empty,
                AccountId = accountId,
                Currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency,
                ExpiresDate = expiresDate,
                SiteId = siteId
            }, "SMDatabase");

            // The reference is assigned by the sequence inside the procedure; the newest quote
            // for this account is the one just created.
            return GetQuotes(siteId)
                .Where(x => x.AccountId == accountId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();
        }

        public void UpdateStatus(int quoteId, string status, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spQuote_UpdateStatus",
                new { Id = quoteId, Status = status, SiteId = siteId }, "SMDatabase");
        }

        public void AddQuoteLine(int quoteId, int productId, int quantity, decimal listPrice, int discountPct, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spQuoteLine_Insert", new
            {
                Id = 0,
                QuoteId = quoteId,
                ProductId = productId,
                Quantity = quantity,
                ListPrice = listPrice,
                DiscountPct = discountPct,
                SiteId = siteId
            }, "SMDatabase");
        }

        public bool DeleteQuoteLine(int quoteId, int lineId, int siteId)
        {
            // The procedure returns its row count, and takes the quote and the site as part of
            // the predicate, so a line id belonging to another quote — or to another store —
            // deletes nothing and reports false rather than reading as a success.
            return _sqlDataAccess.LoadData<int, dynamic>("dbo.spQuoteLine_Delete", new
            {
                Id = lineId,
                QuoteId = quoteId,
                SiteId = siteId
            }, "SMDatabase").FirstOrDefault() > 0;
        }
    }
}
