using Dapper;
using System.Data;
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

        public List<QuoteModel> GetQuotesForAccount(int accountId, int siteId)
        {
            return _sqlDataAccess.LoadData<QuoteModel, dynamic>(
                "dbo.spQuote_GetByAccount", new { AccountId = accountId, SiteId = siteId },
                "SMDatabase");
        }

        public QuoteModel GetQuoteForAccount(string reference, int accountId, int siteId)
        {
            return _sqlDataAccess.LoadData<QuoteModel, dynamic>(
                "dbo.spQuote_GetForAccount",
                new { Reference = reference, AccountId = accountId, SiteId = siteId },
                "SMDatabase").FirstOrDefault();
        }

        public List<QuoteLineModel> GetQuoteLinesForAccount(int quoteId, int accountId, int siteId)
        {
            return _sqlDataAccess.LoadData<QuoteLineModel, dynamic>(
                "dbo.spQuoteLine_GetForAccount",
                new { QuoteId = quoteId, AccountId = accountId, SiteId = siteId },
                "SMDatabase");
        }

        public bool RejectForAccount(int quoteId, int accountId, int siteId, string reason)
        {
            // The procedure reports its row count, so a quote a colleague already decided
            // is distinguishable from one this call rejected.
            return _sqlDataAccess.LoadData<int, dynamic>("dbo.spQuote_Reject", new
            {
                QuoteId = quoteId,
                AccountId = accountId,
                SiteId = siteId,
                Reason = reason
            }, "SMDatabase").FirstOrDefault() > 0;
        }

        public QuoteModel SubmitRequest(QuoteRequest request)
        {
            var table = BuildRequestTable(request.Lines);

            // The reference comes back through a SELECT rather than the output parameter,
            // because SaveData passes an anonymous object and Dapper cannot write back
            // through one. Re-querying for the store's newest quote would hand this caller
            // another customer's under two concurrent submits.
            var reference = _sqlDataAccess.LoadData<string, dynamic>(
                "dbo.spQuote_SubmitRequest",
                new
                {
                    request.ContactId,
                    request.SiteId,
                    Lines = table.AsTableValuedParameter("dbo.QuoteRequestLine"),
                    request.CustomerNote,
                    ExpiresDate = (DateTime?)null,
                    request.BasketId,
                    Id = 0,
                    Reference = string.Empty
                },
                "SMDatabase").FirstOrDefault();

            return string.IsNullOrEmpty(reference)
                ? null
                : GetQuoteByReference(reference, request.SiteId);
        }

        // Column order must match dbo.QuoteRequestLine - table-valued parameters bind by
        // ordinal, not by name.
        private static DataTable BuildRequestTable(IEnumerable<QuoteRequestLine> lines)
        {
            var table = new DataTable();
            table.Columns.Add("ProductId", typeof(int));
            table.Columns.Add("Quantity", typeof(int));
            table.Columns.Add("ListPrice", typeof(decimal));
            table.Columns.Add("DiscountPct", typeof(decimal));
            table.Columns.Add("NetPrice", typeof(decimal));

            // The type is keyed on ProductId, so a basket that somehow held a product twice
            // would fail the whole submit. UQ_BasketLine_Product makes that impossible, and
            // this keeps it impossible for any other caller.
            foreach (var line in lines.GroupBy(line => line.ProductId).Select(group => group.Last()))
            {
                table.Rows.Add(
                    line.ProductId, line.Quantity, line.ListPrice, line.DiscountPct, line.NetPrice);
            }

            return table;
        }

        public void UpdateStatus(int quoteId, string status, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spQuote_UpdateStatus",
                new { Id = quoteId, Status = status, SiteId = siteId }, "SMDatabase");
        }

        public void AddQuoteLine(int quoteId, int productId, int quantity, decimal listPrice,
            decimal discountPct, int siteId, decimal? netPrice = null)
        {
            _sqlDataAccess.SaveData("dbo.spQuoteLine_Insert", new
            {
                Id = 0,
                QuoteId = quoteId,
                ProductId = productId,
                Quantity = quantity,
                ListPrice = listPrice,
                DiscountPct = discountPct,
                SiteId = siteId,
                NetPrice = netPrice
            }, "SMDatabase");
        }

        public bool UpdateQuoteLine(int quoteId, int lineId, int quantity, decimal discountPct,
            int siteId, decimal? netPrice = null)
        {
            // Row count again, and for one more reason than the delete has: the procedure also
            // refuses a line on an accepted quote, so "nothing changed" is an answer the portal
            // has to be able to give rather than a silent no-op.
            return _sqlDataAccess.LoadData<int, dynamic>("dbo.spQuoteLine_Update", new
            {
                Id = lineId,
                QuoteId = quoteId,
                SiteId = siteId,
                Quantity = quantity,
                DiscountPct = discountPct,
                NetPrice = netPrice
            }, "SMDatabase").FirstOrDefault() > 0;
        }

        public bool Price(int quoteId, int siteId)
        {
            // A refused claim means the customer decided the quote first. Distinguished from a
            // failure the whole way out, exactly as a refused conversion is.
            return _sqlDataAccess.LoadData<int, dynamic>("dbo.spQuote_Price", new
            {
                QuoteId = quoteId,
                SiteId = siteId
            }, "SMDatabase").FirstOrDefault() > 0;
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
