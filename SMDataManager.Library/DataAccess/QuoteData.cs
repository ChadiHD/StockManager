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

        public List<QuoteModel> GetQuotes()
        {
            return _sqlDataAccess.LoadData<QuoteModel, dynamic>(
                "dbo.spQuote_GetAll", new { }, "SMDatabase");
        }

        public QuoteModel GetQuoteByReference(string reference)
        {
            return _sqlDataAccess.LoadData<QuoteModel, dynamic>(
                "dbo.spQuote_GetByReference", new { Reference = reference }, "SMDatabase").FirstOrDefault();
        }

        public List<QuoteLineModel> GetQuoteLines(int quoteId)
        {
            return _sqlDataAccess.LoadData<QuoteLineModel, dynamic>(
                "dbo.spQuoteLine_GetByQuote", new { QuoteId = quoteId }, "SMDatabase");
        }

        public QuoteModel CreateQuote(int accountId, string currency, DateTime? expiresDate)
        {
            _sqlDataAccess.SaveData("dbo.spQuote_Insert", new
            {
                Id = 0,
                Reference = string.Empty,
                AccountId = accountId,
                Currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency,
                ExpiresDate = expiresDate
            }, "SMDatabase");

            // The reference is assigned by the sequence inside the procedure; the newest quote
            // for this account is the one just created.
            return GetQuotes()
                .Where(x => x.AccountId == accountId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();
        }

        public void UpdateStatus(int quoteId, string status)
        {
            _sqlDataAccess.SaveData("dbo.spQuote_UpdateStatus",
                new { Id = quoteId, Status = status }, "SMDatabase");
        }

        public void AddQuoteLine(int quoteId, int productId, int quantity, decimal listPrice, int discountPct)
        {
            _sqlDataAccess.SaveData("dbo.spQuoteLine_Insert", new
            {
                Id = 0,
                QuoteId = quoteId,
                ProductId = productId,
                Quantity = quantity,
                ListPrice = listPrice,
                DiscountPct = discountPct
            }, "SMDatabase");
        }

        public bool DeleteQuoteLine(int quoteId, int lineId)
        {
            // The procedure returns its row count, and takes the quote id as part of the
            // predicate, so a line id belonging to another quote deletes nothing and reports
            // false rather than reading as a success.
            return _sqlDataAccess.LoadData<int, dynamic>("dbo.spQuoteLine_Delete", new
            {
                Id = lineId,
                QuoteId = quoteId
            }, "SMDatabase").FirstOrDefault() > 0;
        }
    }
}
