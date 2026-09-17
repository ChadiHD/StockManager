using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// What a basket will accept, who can read it, and what happens to it at sign-in.
/// </summary>
/// <remarks>
/// Against a real database because the interesting parts are all database parts: the visibility
/// check that decides what may be added runs through <c>dbo.fnCatalog_VisibleProducts</c>, the
/// merge at sign-in is one transaction over two baskets, and the predicate that stops an
/// anonymous visitor reading a claimed basket is one line of a <c>WHERE</c> clause with no
/// symptom when it is missing except that the wrong person sees a list.
///
/// None of these tests provokes a procedure's <c>CATCH</c>: <c>spBasket_AddLine</c> and
/// <c>spBasket_Claim</c> raise their refusals before opening a transaction, on purpose, so a
/// caller's own transaction survives them. See the remarks on
/// <see cref="QuoteAcceptanceTests"/> for why that matters here.
///
/// Skips without SMDATABASE_TEST_CONNECTION; see <see cref="TestDatabase"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class BasketQueryTests
{
    private const int NotThisStoresBasket = 50020;
    private const int ProductNotAvailable = 50021;
    private const int NotThisStoresContact = 50022;

    [SkippableFact]
    public void AddingTheSameProductTwiceRaisesTheQuantityRatherThanAddingALine()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = BasketScenario.Create(connection, transaction);
            var basket = store.Ensure();

            store.Add(basket, store.ProductId, 2);
            store.Add(basket, store.ProductId, 3);

            var lines = store.Lines(basket);

            lines.Should().ContainSingle(
                "UQ_BasketLine_Product is what makes this expressible as one statement");
            lines[0].Quantity.Should().Be(5);
        }
    }

    [SkippableFact]
    public void AnAbsurdQuantityIsCappedRatherThanStored()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = BasketScenario.Create(connection, transaction);
            var basket = store.Ensure();

            store.Add(basket, store.ProductId, int.MaxValue);

            // Quantity * NetPrice is money arithmetic, and int.MaxValue of anything overflows a
            // line total long before it becomes an order somebody has to fulfil.
            store.Lines(basket)[0].Quantity.Should().Be(9999);
        }
    }

    [SkippableFact]
    public void AProductFromAnotherStoreCannotBeAdded()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var mine = BasketScenario.Create(connection, transaction);
            var theirs = BasketScenario.Create(connection, transaction);

            var basket = mine.Ensure();

            // A ProductId arrives in a form post, so the page it came from is not evidence.
            // Without the check inside the procedure, a basket could be filled with another
            // tenant's catalog and then priced and quoted from it.
            var add = () => mine.Add(basket, theirs.ProductId, 1);

            add.Should().Throw<SqlException>().Which.Number.Should().Be(ProductNotAvailable);
            mine.Lines(basket).Should().BeEmpty();
        }
    }

    [SkippableFact]
    public void ADelistedProductCannotBeAdded()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = BasketScenario.Create(connection, transaction);
            var basket = store.Ensure();

            store.Delist();

            // Through fnCatalog_VisibleProducts, so "available" means here what it means on the
            // listing, the facet rail and the detail page — one definition, not four.
            var add = () => store.Add(basket, store.ProductId, 1);

            add.Should().Throw<SqlException>().Which.Number.Should().Be(ProductNotAvailable);
        }
    }

    [SkippableFact]
    public void ALineWhoseProductLaterVanishesIsFlaggedAndNotDropped()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = BasketScenario.Create(connection, transaction);
            var basket = store.Ensure();

            store.Add(basket, store.ProductId, 4);
            store.Lines(basket)[0].Available.Should().BeTrue();

            store.Delist();

            // Returned, not dropped. A basket that silently loses rows is one a customer cannot
            // reason about — they came back for four of something and found three lines — and
            // the submit path has to be able to refuse the line explicitly rather than never
            // learn it existed.
            var lines = store.Lines(basket);

            lines.Should().ContainSingle();
            lines[0].Quantity.Should().Be(4);
            lines[0].Available.Should().BeFalse();
        }
    }

    [SkippableFact]
    public void SettingAQuantityToZeroRemovesTheLine()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = BasketScenario.Create(connection, transaction);
            var basket = store.Ensure();

            store.Add(basket, store.ProductId, 2);

            // What a customer typing 0 into a quantity box means. A box that refused it would
            // need a separate remove button beside every row.
            store.SetQuantity(basket, store.ProductId, 0).Should().Be(1);
            store.Lines(basket).Should().BeEmpty();
        }
    }

    [SkippableFact]
    public void AnAnonymousVisitorCannotReadABasketThatHasBeenClaimed()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = BasketScenario.Create(connection, transaction);
            var basket = store.Ensure();

            store.Add(basket, store.ProductId, 1);
            store.Claim();

            // A claimed basket keeps its token, so the cookie left in the browser after
            // sign-out still names it. Without the ContactId IS NULL predicate in
            // spBasket_Find, the next person at a shared machine would be shown this
            // customer's list. BasketService clears the cookie too; this is the half that does
            // not depend on a response header arriving.
            store.FindAnonymous().Should().BeNull();
            store.FindForContact().Should().Be(basket);
        }
    }

    [SkippableFact]
    public void SigningInWithNoBasketOfTheirOwnJustTakesTheBrowsersOne()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = BasketScenario.Create(connection, transaction);
            var basket = store.Ensure();

            store.Add(basket, store.ProductId, 3);

            store.Claim().Should().Be(basket, "there was nothing to merge it into");
            store.Lines(basket)[0].Quantity.Should().Be(3);
        }
    }

    [SkippableFact]
    public void SigningInMergesTheBrowsersBasketIntoTheirOwn()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = BasketScenario.Create(connection, transaction);

            // What they built while signed in on another machine.
            var theirs = store.EnsureForContact();
            store.Add(theirs, store.ProductId, 2);
            store.Add(theirs, store.SecondProductId, 1);

            // And what is in this browser, anonymously.
            var browser = store.Ensure();
            store.Add(browser, store.ProductId, 3);

            store.Claim().Should().Be(theirs,
                "the contact's basket survives, so its id is stable across browsers");

            var lines = store.Lines(theirs);

            lines.Should().HaveCount(2);
            // Added rather than replaced: a customer who put three on their phone and two on
            // their laptop wants five.
            lines.Single(line => line.ProductId == store.ProductId).Quantity.Should().Be(5);
            lines.Single(line => line.ProductId == store.SecondProductId).Quantity.Should().Be(1);

            // And the source basket is gone, with its lines, through the cascade. Left behind,
            // it would be merged again on the next sign-in and double the quantities.
            store.Exists(browser).Should().BeFalse();
        }
    }

    [SkippableFact]
    public void AContactFromAnotherStoreCannotClaimThisStoresBasket()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var mine = BasketScenario.Create(connection, transaction);
            var theirs = BasketScenario.Create(connection, transaction);

            mine.Ensure();

            // Contact carries no SiteId of its own, only its account's, so the chain has to be
            // walked. The shared Data Protection key ring makes a cookie from one host readable
            // by the other, which is what turns this from hypothetical into a shape.
            var claim = () => mine.ClaimAs(theirs.ContactId);

            claim.Should().Throw<SqlException>().Which.Number.Should().Be(NotThisStoresContact);
        }
    }

    [SkippableFact]
    public void ABasketFromAnotherStoreRefusesEveryChange()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var mine = BasketScenario.Create(connection, transaction);
            var theirs = BasketScenario.Create(connection, transaction);

            var stranger = theirs.Ensure();

            // A basket id is sequential, so it is guessable. Every mutation takes the site as a
            // predicate rather than trusting the caller to have resolved the basket.
            var add = () => mine.Add(stranger, mine.ProductId, 1);

            add.Should().Throw<SqlException>().Which.Number.Should().Be(NotThisStoresBasket);
        }
    }

    [SkippableFact]
    public void ThePurgeTakesOldAnonymousBasketsAndLeavesTheRest()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = BasketScenario.Create(connection, transaction);

            var stale = store.Ensure();
            store.Age(stale, days: 60);

            var fresh = store.Ensure(token: "fresh");

            var claimed = store.EnsureForContact();
            store.Age(claimed, days: 60);

            store.Purge(olderThanDays: 30);

            store.Exists(stale).Should().BeFalse();
            store.Exists(fresh).Should().BeTrue();
            // Never swept. A signed-in customer's basket is the one somebody expects to still
            // be there when they come back next month.
            store.Exists(claimed).Should().BeTrue();
        }
    }

    /// <summary>
    /// A store with two visible products and one contact, inside a transaction that is never
    /// committed.
    /// </summary>
    private sealed class BasketScenario
    {
        private const string AnonymousToken = "basket-token-for-the-anonymous-visitor-aa";

        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly string _runId;

        private BasketScenario(
            SqlConnection connection, SqlTransaction transaction, string runId,
            int siteId, int productId, int secondProductId, int contactId)
        {
            _connection = connection;
            _transaction = transaction;
            _runId = runId;
            SiteId = siteId;
            ProductId = productId;
            SecondProductId = secondProductId;
            ContactId = contactId;
        }

        public int SiteId { get; }
        public int ProductId { get; }
        public int SecondProductId { get; }
        public int ContactId { get; }

        private string Token(string? suffix = null) =>
            suffix is null ? $"{_runId}-{AnonymousToken}"[..43] : $"{_runId}-{suffix}-token-aaaaaaaaaaaaaaaaaaaaaa"[..43];

        public static BasketScenario Create(SqlConnection connection, SqlTransaction transaction)
        {
            // SiteKey and Domain are unique, and a rolled-back transaction still collides with
            // a concurrent one that has not rolled back yet.
            var runId = Guid.NewGuid().ToString("N")[..12];

            var siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Basket test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);
                """,
                ("@key", $"basket-{runId}"),
                ("@domain", $"{runId}.basket.invalid"));

            // A product is only visible through a category this site maps, so the mapping is
            // part of the fixture rather than an optional extra.
            var feedValue = $"BasketCategory-{runId}";

            var categoryId = Scalar(connection, transaction, """
                INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@siteId, @slug, N'Basket test category', 1, 1);
                """,
                ("@siteId", siteId),
                ("@slug", $"basket-{runId}"));

            Execute(connection, transaction, """
                INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId)
                VALUES (@siteId, @feedValue, @categoryId);
                """,
                ("@siteId", siteId),
                ("@feedValue", feedValue),
                ("@categoryId", categoryId));

            var productId = AddProduct(connection, transaction, $"BSK-{runId}-1", feedValue);
            var secondProductId = AddProduct(connection, transaction, $"BSK-{runId}-2", feedValue);

            var accountId = Scalar(connection, transaction, """
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                         PaymentTerms, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, N'Basket test customer', N'EUR', N'Card',
                        N'Prepaid', 0, N'Approved', @siteId);
                """,
                ("@reference", $"AC-B{runId[..6]}"),
                ("@siteId", siteId));

            var contactId = Scalar(connection, transaction, """
                INSERT INTO dbo.Contact (AccountId, FirstName, LastName, Email, RoleInAccount,
                                         IsPrimary, [Status])
                OUTPUT INSERTED.Id
                VALUES (@accountId, N'Basket', N'Buyer', @email, N'Buyer', 1, N'Active');
                """,
                ("@accountId", accountId),
                ("@email", $"buyer-{runId}@basket.invalid"));

            return new BasketScenario(
                connection, transaction, runId, siteId, productId, secondProductId, contactId);
        }

        private static int AddProduct(
            SqlConnection connection, SqlTransaction transaction, string sku, string feedValue) =>
            Scalar(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Cost, Sku,
                                         Category, QuantityInStock, Published, Delisted)
                OUTPUT INSERTED.Id
                VALUES (N'Basket fixture', N'Basket fixture.', 100, 60, @sku,
                        @feedValue, 5, 1, 0);
                """,
                ("@sku", sku),
                ("@feedValue", feedValue));

        public int Ensure(string? token = null) => Scalar(_connection, _transaction, """
            EXEC dbo.spBasket_Ensure @SiteId = @siteId, @Token = @token, @ContactId = NULL;
            """, ("@siteId", SiteId), ("@token", Token(token)));

        public int EnsureForContact() => Scalar(_connection, _transaction, """
            EXEC dbo.spBasket_Ensure @SiteId = @siteId, @Token = @token, @ContactId = @contactId;
            """,
            ("@siteId", SiteId),
            ("@token", Token("contact")),
            ("@contactId", ContactId));

        public void Add(int basketId, int productId, int quantity) =>
            Execute(_connection, _transaction, """
                EXEC dbo.spBasket_AddLine
                    @BasketId = @basketId, @SiteId = @siteId,
                    @ProductId = @productId, @Quantity = @quantity;
                """,
                ("@basketId", basketId),
                ("@siteId", SiteId),
                ("@productId", productId),
                ("@quantity", quantity));

        public int SetQuantity(int basketId, int productId, int quantity) =>
            Scalar(_connection, _transaction, """
                EXEC dbo.spBasket_SetQuantity
                    @BasketId = @basketId, @SiteId = @siteId,
                    @ProductId = @productId, @Quantity = @quantity;
                """,
                ("@basketId", basketId),
                ("@siteId", SiteId),
                ("@productId", productId),
                ("@quantity", quantity));

        public sealed record Line(int ProductId, string Sku, int Quantity, bool Available);

        public List<Line> Lines(int basketId)
        {
            using var command = new SqlCommand(
                "EXEC dbo.spBasket_GetLines @BasketId = @basketId, @SiteId = @siteId;",
                _connection, _transaction);

            command.Parameters.AddWithValue("@basketId", basketId);
            command.Parameters.AddWithValue("@siteId", SiteId);

            using var reader = command.ExecuteReader();

            var lines = new List<Line>();

            while (reader.Read())
            {
                lines.Add(new Line(
                    reader.GetInt32(reader.GetOrdinal("ProductId")),
                    reader.GetString(reader.GetOrdinal("Sku")),
                    reader.GetInt32(reader.GetOrdinal("Quantity")),
                    reader.GetBoolean(reader.GetOrdinal("Available"))));
            }

            return lines;
        }

        public int? Claim() => ClaimAs(ContactId);

        public int? ClaimAs(int contactId)
        {
            using var command = new SqlCommand("""
                EXEC dbo.spBasket_Claim @SiteId = @siteId, @Token = @token, @ContactId = @contactId;
                """, _connection, _transaction);

            command.Parameters.AddWithValue("@siteId", SiteId);
            command.Parameters.AddWithValue("@token", Token());
            command.Parameters.AddWithValue("@contactId", contactId);

            var result = command.ExecuteScalar();

            return result is null or DBNull ? null : (int)result;
        }

        public int? FindAnonymous() => Find(Token(), contactId: null);

        public int? FindForContact() => Find(Token(), ContactId);

        private int? Find(string token, int? contactId)
        {
            using var command = new SqlCommand(
                "EXEC dbo.spBasket_Find @SiteId = @siteId, @Token = @token, @ContactId = @contactId;",
                _connection, _transaction);

            command.Parameters.AddWithValue("@siteId", SiteId);
            command.Parameters.AddWithValue("@token", token);
            command.Parameters.AddWithValue("@contactId", (object?)contactId ?? DBNull.Value);

            using var reader = command.ExecuteReader();

            return reader.Read() ? reader.GetInt32(reader.GetOrdinal("Id")) : null;
        }

        /// <summary>What a feed does when the distributor stops listing a product.</summary>
        public void Delist() => Execute(_connection, _transaction, """
            UPDATE dbo.Product SET Delisted = 1 WHERE Id = @productId;
            """, ("@productId", ProductId));

        public void Age(int basketId, int days) => Execute(_connection, _transaction, """
            UPDATE dbo.Basket
            SET UpdatedUtc = DATEADD(DAY, -@days, SYSUTCDATETIME())
            WHERE Id = @basketId;
            """, ("@days", days), ("@basketId", basketId));

        public int Purge(int olderThanDays) => Scalar(_connection, _transaction, """
            EXEC dbo.spBasket_PurgeAbandoned @OlderThanDays = @days, @Take = 500;
            """, ("@days", olderThanDays));

        public bool Exists(int basketId) => Scalar(_connection, _transaction, """
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
