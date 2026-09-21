using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// What <c>spQuote_SubmitRequest</c> writes, and what it refuses to write.
/// </summary>
/// <remarks>
/// Against a real database because the whole procedure is a transaction over three tables plus
/// a derivation: the account comes from the contact and the currency from the account, neither
/// taken from the caller, and the basket is deleted in the same transaction as the quote it
/// produced. A substituted data layer cannot tell you that a rolled-back submit leaves the
/// customer's basket where they left it.
///
/// The refusals (50022, 50030, 50031) are raised before the transaction opens, so they are safe
/// anywhere in a test. The <c>CATCH</c> is only reachable by a genuine write failure. See the
/// remarks on <see cref="QuoteAcceptanceTests"/> for why that distinction matters here.
///
/// Skips without SMDATABASE_TEST_CONNECTION; see <see cref="TestDatabase"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class QuoteSubmissionTests
{
    private const int NotThisStoresContact = 50022;
    private const int NothingToQuote = 50030;
    private const int ProductNotSoldHere = 50031;

    [SkippableFact]
    public void ASubmitWritesTheQuoteItsLinesAndEmptiesTheBasket()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = SubmissionScenario.Create(connection, transaction);
            var basket = store.BasketWith((store.ProductId, 3), (store.SecondProductId, 1));

            var reference = store.Submit(basket, note: "Needed before month end");

            reference.Should().StartWith("QT-");

            var quote = store.Quote(reference!);

            // Derived, not passed: a session proves a contact and everything else follows.
            quote.AccountId.Should().Be(store.AccountId);
            quote.SiteId.Should().Be(store.SiteId);
            quote.Status.Should().Be("Requested");
            quote.Currency.Should().Be("EUR");
            quote.CustomerNote.Should().Be("Needed before month end");

            var lines = store.Lines(reference!);

            lines.Should().HaveCount(2);
            lines.Single(line => line.ProductId == store.ProductId).Quantity.Should().Be(3);

            // In the same transaction as the quote. A basket that outlived its submit would be
            // sitting there inviting the customer to send the same request again.
            store.BasketExists(basket).Should().BeFalse();
        }
    }

    [SkippableFact]
    public void ThePriceTheCallerStatedIsTheOneStored()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = SubmissionScenario.Create(connection, transaction);
            var basket = store.BasketWith((store.ProductId, 2));

            // A price the site's margin floor held up: no discount off a list of 100 produces
            // 106.40, so nothing could reconstruct it from (ListPrice, DiscountPct). The whole
            // reason spQuoteLine_Insert stopped deriving net prices.
            var reference = store.Submit(basket, listPrice: 100m, discountPct: 12.50m, netPrice: 106.40m);

            var line = store.Lines(reference!).Single();

            line.ListPrice.Should().Be(100m);
            line.DiscountPct.Should().Be(12.50m);
            line.NetPrice.Should().Be(106.40m);
        }
    }

    [SkippableFact]
    public void AnEmptyRequestIsRefusedAndTheBasketIsLeftAlone()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = SubmissionScenario.Create(connection, transaction);
            var basket = store.BasketWith((store.ProductId, 1));

            // A quote with no lines is a request sales cannot answer, and deleting the basket
            // on the way to producing one would throw the customer's list away for nothing.
            var submit = () => store.SubmitNothing(basket);

            submit.Should().Throw<SqlException>().Which.Number.Should().Be(NothingToQuote);
            store.BasketExists(basket).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void AContactFromAnotherStoreCannotSubmitHere()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var mine = SubmissionScenario.Create(connection, transaction);
            var theirs = SubmissionScenario.Create(connection, transaction);

            var basket = mine.BasketWith((mine.ProductId, 1));

            // Contact carries no SiteId of its own, only its account's, so the chain has to be
            // walked — the same check spBasket_Claim makes, for the same reason.
            var submit = () => mine.SubmitAs(theirs.ContactId, basket);

            submit.Should().Throw<SqlException>().Which.Number.Should().Be(NotThisStoresContact);
        }
    }

    [SkippableFact]
    public void AProductThisStoreDoesNotSellCannotReachAQuoteLine()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var mine = SubmissionScenario.Create(connection, transaction);
            var theirs = SubmissionScenario.Create(connection, transaction);

            var basket = mine.BasketWith((mine.ProductId, 1));

            // The caller resolved its lines through fnCatalog_VisibleProducts already, and
            // spBasket_AddLine would have refused this product too. This is the predicate that
            // holds without either of those having run — one atomic write, checked on its own
            // terms.
            var submit = () => mine.SubmitProduct(basket, theirs.ProductId);

            submit.Should().Throw<SqlException>().Which.Number.Should().Be(ProductNotSoldHere);
            mine.BasketExists(basket).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void AnotherCustomersBasketIsNotDeletedByASubmit()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var mine = SubmissionScenario.Create(connection, transaction);
            var theirs = SubmissionScenario.Create(connection, transaction);

            var strangersBasket = theirs.BasketWith((theirs.ProductId, 1));
            var basket = mine.BasketWith((mine.ProductId, 1));

            // A valid request of this store's own product, but naming a basket id that
            // belongs to somebody else. @BasketId arrives from the caller, so the DELETE is
            // scoped to the site and the contact as well: a guessed id deletes nothing, and
            // the quote is still written because the lines were fine.
            mine.SubmitProduct(strangersBasket, mine.ProductId).Should().StartWith("QT-");

            theirs.BasketExists(strangersBasket).Should().BeTrue();
            mine.BasketExists(basket).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void ABlankNoteIsStoredAsNothing()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = SubmissionScenario.Create(connection, transaction);
            var basket = store.BasketWith((store.ProductId, 1));

            var reference = store.Submit(basket, note: "   ");

            // A customer who left the box alone wrote no note, and a quote document that quoted
            // one anyway prints an empty heading.
            store.Quote(reference!).CustomerNote.Should().BeNull();
        }
    }

    /// <summary>
    /// A store with two sellable products, an approved account, a contact and a basket.
    /// </summary>
    private sealed class SubmissionScenario
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly string _runId;

        private SubmissionScenario(
            SqlConnection connection, SqlTransaction transaction, string runId,
            int siteId, int accountId, int contactId, int productId, int secondProductId)
        {
            _connection = connection;
            _transaction = transaction;
            _runId = runId;
            SiteId = siteId;
            AccountId = accountId;
            ContactId = contactId;
            ProductId = productId;
            SecondProductId = secondProductId;
        }

        public int SiteId { get; }
        public int AccountId { get; }
        public int ContactId { get; }
        public int ProductId { get; }
        public int SecondProductId { get; }

        public static SubmissionScenario Create(SqlConnection connection, SqlTransaction transaction)
        {
            var runId = Guid.NewGuid().ToString("N")[..12];

            var siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Submit test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);
                """,
                ("@key", $"submit-{runId}"),
                ("@domain", $"{runId}.submit.invalid"));

            // The CategoryMapping is what makes a product this store's, and it is the predicate
            // spQuote_SubmitRequest checks, so it is part of the fixture rather than an extra.
            var feedValue = $"SubmitCategory-{runId}";

            var categoryId = Scalar(connection, transaction, """
                INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@siteId, @slug, N'Submit test category', 1, 1);
                """,
                ("@siteId", siteId),
                ("@slug", $"submit-{runId}"));

            Execute(connection, transaction, """
                INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId)
                VALUES (@siteId, @feedValue, @categoryId);
                """,
                ("@siteId", siteId),
                ("@feedValue", feedValue),
                ("@categoryId", categoryId));

            var productId = AddProduct(connection, transaction, $"SUB-{runId}-1", feedValue);
            var secondProductId = AddProduct(connection, transaction, $"SUB-{runId}-2", feedValue);

            var accountId = Scalar(connection, transaction, """
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                         PaymentTerms, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, N'Submit test customer', N'EUR', N'Credit',
                        N'Net 30', 10000, N'Approved', @siteId);
                """,
                ("@reference", $"AC-S{runId[..6]}"),
                ("@siteId", siteId));

            var contactId = Scalar(connection, transaction, """
                INSERT INTO dbo.Contact (AccountId, FirstName, LastName, Email, RoleInAccount,
                                         IsPrimary, [Status])
                OUTPUT INSERTED.Id
                VALUES (@accountId, N'Submit', N'Buyer', @email, N'Buyer', 1, N'Active');
                """,
                ("@accountId", accountId),
                ("@email", $"buyer-{runId}@submit.invalid"));

            return new SubmissionScenario(
                connection, transaction, runId, siteId, accountId, contactId,
                productId, secondProductId);
        }

        private static int AddProduct(
            SqlConnection connection, SqlTransaction transaction, string sku, string feedValue) =>
            Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Cost, Sku,
                                         Category, QuantityInStock, Published, Delisted)
                OUTPUT INSERTED.Id
                VALUES (N'Submit fixture', N'Submit fixture.', 100, 60, @sku,
                        @feedValue, 5, 1, 0);
                """,
                ("@sku", sku),
                ("@feedValue", feedValue));

        /// <summary>A basket belonging to this store's contact, holding the given lines.</summary>
        public int BasketWith(params (int ProductId, int Quantity)[] lines)
        {
            var basketId = Scalar(_connection, _transaction, """
                INSERT INTO dbo.Basket (SiteId, ContactId, Token)
                OUTPUT INSERTED.Id
                VALUES (@siteId, @contactId, @token);
                """,
                ("@siteId", SiteId),
                ("@contactId", ContactId),
                ("@token", $"{_runId}-{Guid.NewGuid():N}"[..43]));

            foreach (var (productId, quantity) in lines)
            {
                Execute(_connection, _transaction, """
                    INSERT INTO dbo.BasketLine (BasketId, ProductId, Quantity)
                    VALUES (@basketId, @productId, @quantity);
                    """,
                    ("@basketId", basketId),
                    ("@productId", productId),
                    ("@quantity", quantity));
            }

            return basketId;
        }

        /// <summary>
        /// Submits whatever the basket holds, priced as stated.
        /// </summary>
        /// <remarks>
        /// Reads the basket rather than taking a line list, because that is what
        /// QuoteSubmissionService does: the caller resolves a price per basket line and sends
        /// those. A fixture that submitted a fixed line would pass whatever the basket held.
        /// </remarks>
        public string? Submit(
            int basketId, string? note = null,
            decimal listPrice = 100m, decimal discountPct = 0m, decimal netPrice = 100m) =>
            SubmitLines(ContactId, basketId, note,
                BasketContents(basketId)
                    .Select(line => (line.ProductId, line.Quantity, listPrice, discountPct, netPrice))
                    .ToArray());

        private List<(int ProductId, int Quantity)> BasketContents(int basketId)
        {
            using var command = new SqlCommand("""
                SELECT [ProductId], [Quantity] FROM dbo.BasketLine
                WHERE [BasketId] = @basketId ORDER BY [Id];
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@basketId", basketId);

            using var reader = command.ExecuteReader();

            var lines = new List<(int, int)>();

            while (reader.Read())
            {
                lines.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }

            return lines;
        }

        public string? SubmitAs(int contactId, int basketId) =>
            SubmitLines(contactId, basketId, null, [(ProductId, 1, 100m, 0m, 100m)]);

        public string? SubmitProduct(int basketId, int productId) =>
            SubmitLines(ContactId, basketId, null, [(productId, 1, 100m, 0m, 100m)]);

        public string? SubmitNothing(int basketId) =>
            SubmitLines(ContactId, basketId, null, []);

        private string? SubmitLines(
            int contactId, int basketId, string? note,
            (int ProductId, int Quantity, decimal ListPrice, decimal DiscountPct, decimal NetPrice)[] lines)
        {
            // The table type is built in T-SQL rather than passed as a DataTable, because what
            // is under test is the procedure and not Dapper's binding of it.
            var values = lines.Length == 0
                ? string.Empty
                : "INSERT INTO @lines VALUES " + string.Join(", ", lines.Select(line =>
                    $"({line.ProductId}, {line.Quantity}, {line.ListPrice}, {line.DiscountPct}, {line.NetPrice})")) + ";";

            using var command = new SqlCommand($"""
                DECLARE @lines dbo.QuoteRequestLine;
                {values}

                DECLARE @Id int, @Reference nvarchar(20);

                EXEC dbo.spQuote_SubmitRequest
                    @ContactId = @contactId, @SiteId = @siteId, @Lines = @lines,
                    @CustomerNote = @note, @BasketId = @basketId,
                    @Id = @Id OUTPUT, @Reference = @Reference OUTPUT;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@contactId", contactId);
            command.Parameters.AddWithValue("@siteId", SiteId);
            command.Parameters.AddWithValue("@basketId", basketId);
            command.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);

            var result = command.ExecuteScalar();

            return result is null or DBNull ? null : (string)result;
        }

        public sealed record QuoteRow(
            int AccountId, int SiteId, string Status, string Currency, string? CustomerNote);

        public QuoteRow Quote(string reference)
        {
            using var command = new SqlCommand("""
                SELECT [AccountId], [SiteId], [Status], [Currency], [CustomerNote]
                FROM dbo.Quote WHERE [Reference] = @reference;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@reference", reference);

            using var reader = command.ExecuteReader();

            reader.Read().Should().BeTrue("the submit should have written a quote");

            return new QuoteRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }

        public sealed record LineRow(int ProductId, int Quantity, decimal ListPrice, decimal DiscountPct, decimal NetPrice);

        public List<LineRow> Lines(string reference)
        {
            using var command = new SqlCommand("""
                SELECT l.[ProductId], l.[Quantity], l.[ListPrice], l.[DiscountPct], l.[NetPrice]
                FROM dbo.QuoteLine l
                INNER JOIN dbo.Quote q ON q.[Id] = l.[QuoteId]
                WHERE q.[Reference] = @reference
                ORDER BY l.[Id];
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@reference", reference);

            using var reader = command.ExecuteReader();

            var lines = new List<LineRow>();

            while (reader.Read())
            {
                lines.Add(new LineRow(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4)));
            }

            return lines;
        }

        public bool BasketExists(int basketId) => Scalar(_connection, _transaction, """
            SELECT COUNT(*) FROM dbo.Basket WHERE Id = @basketId;
            """, ("@basketId", basketId)) == 1;

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
