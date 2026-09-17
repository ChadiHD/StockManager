using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// A product the distributor stopped supplying still resolves on the quote that already
/// contains it.
/// </summary>
/// <remarks>
/// This is the promise <c>spProduct_BulkUpsertFromFeed</c> makes when it flags rather than
/// deletes — "so historical quotes still resolve", per the July design — and until now nothing
/// asserted it. It is one well-meant <c>AND p.Delisted = 0</c> away from a customer opening a
/// six-month-old quote and finding blank lines, and that edit would look like a tightening
/// rather than a regression.
///
/// The staleness predicate T4 added is the same hazard arriving by a different route, so it is
/// checked here too: hiding stale stock from the shop window must not reach a quote a customer
/// is holding.
///
/// Against a real database because that is the only place the join lives. Skips without
/// SMDATABASE_TEST_CONNECTION; see <see cref="TestDatabase"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class DelistedProductHistoryTests
{
    [SkippableFact]
    public void ADelistedProductStillResolvesOnAQuoteThatAlreadyHasIt()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var scope = QuoteFixture.Create(connection, transaction);

            scope.Delist();

            var lines = scope.QuoteLines();

            lines.Should().ContainSingle();
            // Not just a row: the SKU and the name come from the Product join, so an empty
            // string here would mean the line rendered without saying what it was for.
            lines[0].Sku.Should().Be(scope.Sku);
            lines[0].Name.Should().NotBeNullOrWhiteSpace();
        }
    }

    [SkippableFact]
    public void AStaleProductStillResolvesOnAQuoteThatAlreadyHasIt()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            // The store hides stale stock, and this product's feed last delivered four days
            // ago — so the catalog will not show it. The quote must be unaffected: a customer
            // holding a quotation has been given a price for a specific thing, and the answer
            // to "the distributor has gone quiet" is a conversation, not a blank line.
            var scope = QuoteFixture.Create(
                connection, transaction, hideStale: true, staleAfterHours: 24, syncedHoursAgo: 96);

            scope.QuoteLines().Should().ContainSingle();
            scope.IsInCatalog().Should().BeFalse("the shop window hides it");
        }
    }

    [SkippableFact]
    public void ADelistedProductIsGoneFromTheCatalogWhileStillOnTheQuote()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var scope = QuoteFixture.Create(connection, transaction);

            scope.IsInCatalog().Should().BeTrue("nothing has happened to it yet");

            scope.Delist();

            // The pair is the point. Either half alone is satisfiable by a mistake: a predicate
            // that hid it everywhere, or one that hid it nowhere.
            scope.IsInCatalog().Should().BeFalse();
            scope.QuoteLines().Should().ContainSingle();
        }
    }

    [SkippableFact]
    public void AQuoteCarryingADelistedProductStillConvertsToAnOrder()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var scope = QuoteFixture.Create(connection, transaction);

            scope.Delist();

            var orderLines = scope.ConvertToOrder();

            /*
            Recorded as the behaviour, not endorsed as the policy.

            spOrder_ConvertFromQuote copies QuoteLine rows with no Delisted predicate, so a
            quote accepted after its product was dropped becomes an order for stock the
            distributor no longer supplies. That is right for the mechanism — refusing here
            would leave the customer's acceptance silently doing nothing — and it is a question
            T5 owns: whether a quote with a delisted line should be acceptable at all, or should
            be re-quoted first. This test exists so that decision is made deliberately rather
            than discovered.
            */
            orderLines.Should().Be(1);
        }
    }

    /// <summary>
    /// An approved account with one quote for one product, inside a transaction that is never
    /// committed.
    /// </summary>
    private sealed class QuoteFixture
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;

        private QuoteFixture(
            SqlConnection connection, SqlTransaction transaction,
            int siteId, int productId, int quoteId, string sku, string staffId)
        {
            _connection = connection;
            _transaction = transaction;
            SiteId = siteId;
            ProductId = productId;
            QuoteId = quoteId;
            Sku = sku;
            StaffId = staffId;
        }

        public int SiteId { get; }
        public int ProductId { get; }
        public int QuoteId { get; }
        public string Sku { get; }
        public string StaffId { get; }

        public static QuoteFixture Create(
            SqlConnection connection,
            SqlTransaction transaction,
            bool hideStale = false,
            int staleAfterHours = 0,
            int? syncedHoursAgo = 1)
        {
            var runId = Guid.NewGuid().ToString("N")[..12];

            var siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Delisting test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, @staleAfter, @hideStale, 1);
                """,
                ("@key", $"delist-{runId}"),
                ("@domain", $"{runId}.delist.invalid"),
                ("@staleAfter", staleAfterHours),
                ("@hideStale", hideStale));

            var feedValue = $"DelistCategory-{runId}";

            var categoryId = Scalar(connection, transaction, """
                INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@siteId, @slug, N'Delisting test category', 1, 1);
                """,
                ("@siteId", siteId),
                ("@slug", $"delist-{runId}"));

            Execute(connection, transaction, """
                INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId)
                VALUES (@siteId, @feedValue, @categoryId);
                """,
                ("@siteId", siteId),
                ("@feedValue", feedValue),
                ("@categoryId", categoryId));

            var sku = $"DEL-{runId}";

            var productId = Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                         QuantityInStock, Published, Delisted, [Source],
                                         Distributor, DistributorSku, LastSynced)
                OUTPUT INSERTED.Id
                VALUES (N'Delisting fixture', N'Delisting fixture.', 100, @sku, @feedValue,
                        5, 1, 0, N'Distributor', N'TestDistributor', @sku, @lastSynced);
                """,
                ("@sku", sku),
                ("@feedValue", feedValue),
                ("@lastSynced", syncedHoursAgo is { } hours
                    ? DateTime.UtcNow.AddHours(-hours)
                    : DBNull.Value));

            var accountId = Scalar(connection, transaction, """
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                         PaymentTerms, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, N'Delisting test customer', N'EUR', N'Card',
                        N'Prepaid', 0, N'Approved', @siteId);
                """,
                ("@reference", $"AC-D{runId[..6]}"),
                ("@siteId", siteId));

            var quoteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, @accountId, N'EUR', N'Requested', @siteId);
                """,
                ("@reference", $"QT-D{runId[..6]}"),
                ("@accountId", accountId),
                ("@siteId", siteId));

            Execute(connection, transaction, """
                INSERT INTO dbo.QuoteLine (QuoteId, ProductId, Quantity, ListPrice, DiscountPct, NetPrice)
                VALUES (@quoteId, @productId, 2, 100, 0, 100);
                """,
                ("@quoteId", quoteId),
                ("@productId", productId));

            // Purchase.StaffId is a foreign key into dbo.User(UserId), so converting a quote
            // needs a real staff row — the same shape of trap as the FK_Account_ApprovedBy bug
            // the end-to-end suite found, where an id-looking string satisfied the compiler and
            // not the database.
            var staffId = $"staff-{runId}";

            Execute(connection, transaction, """
                INSERT INTO dbo.[User] (UserId, FirstName, LastName, EmailAddress)
                VALUES (@staffId, N'Delisting', N'Fixture', @email);
                """,
                ("@staffId", staffId),
                ("@email", $"{runId}@delist.invalid"));

            return new QuoteFixture(connection, transaction, siteId, productId, quoteId, sku, staffId);
        }

        /// <summary>What a feed does when the distributor stops listing a product.</summary>
        public void Delist() => Execute(_connection, _transaction, """
            UPDATE dbo.Product SET Delisted = 1, QuantityInStock = 0 WHERE Id = @productId;
            """, ("@productId", ProductId));

        public sealed record Line(string Sku, string Name);

        public List<Line> QuoteLines()
        {
            using var command = new SqlCommand(
                "EXEC dbo.spQuoteLine_GetByQuote @QuoteId = @quoteId, @SiteId = @siteId;",
                _connection, _transaction);

            command.Parameters.AddWithValue("@quoteId", QuoteId);
            command.Parameters.AddWithValue("@siteId", SiteId);

            using var reader = command.ExecuteReader();

            var lines = new List<Line>();

            while (reader.Read())
            {
                lines.Add(new Line(
                    reader.GetString(reader.GetOrdinal("Sku")),
                    reader.GetString(reader.GetOrdinal("Name"))));
            }

            return lines;
        }

        public bool IsInCatalog()
        {
            using var command = new SqlCommand(
                "EXEC dbo.spCatalog_GetBySku @SiteId = @siteId, @Sku = @sku;",
                _connection, _transaction);

            command.Parameters.AddWithValue("@siteId", SiteId);
            command.Parameters.AddWithValue("@sku", Sku);

            using var reader = command.ExecuteReader();

            return reader.Read();
        }

        /// <summary>Converts the quote and returns how many order lines it produced.</summary>
        public int ConvertToOrder()
        {
            using var convert = new SqlCommand("""
                DECLARE @OrderId int, @OrderRef nvarchar(20);

                EXEC dbo.spOrder_ConvertFromQuote
                    @QuoteId = @quoteId, @StaffId = @staffId,
                    @Id = @OrderId OUTPUT, @Reference = @OrderRef OUTPUT, @SiteId = @siteId;

                SELECT COUNT(*) FROM dbo.PurchaseDetail WHERE PurchaseId = @OrderId;
                """, _connection, _transaction);

            convert.Parameters.AddWithValue("@quoteId", QuoteId);
            convert.Parameters.AddWithValue("@siteId", SiteId);
            convert.Parameters.AddWithValue("@staffId", StaffId);

            return (int)convert.ExecuteScalar()!;
        }

        private static void Execute(
            SqlConnection connection, SqlTransaction transaction, string sql,
            params (string Name, object Value)[] parameters)
        {
            using var command = new SqlCommand(sql, connection, transaction);

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            command.ExecuteNonQuery();
        }

        private static int Scalar(
            SqlConnection connection, SqlTransaction transaction, string sql,
            params (string Name, object Value)[] parameters)
        {
            using var command = new SqlCommand(sql, connection, transaction);

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (int)command.ExecuteScalar()!;
        }
    }
}
