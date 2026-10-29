using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// An order placed from a cart (T12), through <c>spOrder_PlaceFromBasket</c> and the status
/// change that cancels it.
/// </summary>
/// <remarks>
/// The claims that matter here have no failure mode that looks like an error: a guest filed under
/// somebody else's account, the last shirt sold twice, a short order that took half the stock, a
/// cancel that returned it twice. Each is a row count or a quantity.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class CheckoutOrderTests
{
    private const string Token = "guest-token-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [SkippableFact]
    public void AGuestOrderIsAConsumerAccountOfItsOwnAndTakesTheStock()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var shirt = db.Product("SHIRT", price: 40m, stock: 3);
        var basket = db.Basket();

        var placed = db.Place(basket, guest: true, (shirt, 2, 40m));

        placed.ShortSkus.Should().BeNull();
        placed.Reference.Should().StartWith("SO-");
        db.Stock(shirt).Should().Be(1);

        db.Row("""
            SELECT p.Status, CAST(CASE WHEN p.QuoteId IS NULL THEN 1 ELSE 0 END AS bit), a.Kind, a.Status, a.Company,
                   CAST(CASE WHEN c.IdentityUserId IS NULL THEN 1 ELSE 0 END AS bit), pa.Country, p.FinalPrice
            FROM dbo.Purchase p
            JOIN dbo.Account a ON a.Id = p.AccountId
            JOIN dbo.Contact c ON c.Id = p.PlacedByContactId
            JOIN dbo.PurchaseAddress pa ON pa.PurchaseId = p.Id
            WHERE p.Reference = @ref;
            """, ("@ref", placed.Reference))
            .Should().Equal("Awaiting payment", true, "Consumer", "Approved", "Ana Silva", true, "BR", 93.60m);

        db.Scalar<int>("SELECT COUNT(*) FROM dbo.Basket WHERE Id = @b;", ("@b", basket))
            .Should().Be(0, "a cart left full after its order exists invites a second one");
    }

    [SkippableFact]
    public void OnlyTheTokenOpensAGuestsOrderAndOnlyOnItsStore()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var shirt = db.Product("SHIRT", price: 40m, stock: 3);
        var placed = db.Place(db.Basket(), guest: true, (shirt, 1, 40m));

        db.ByToken(Token, db.SiteId).Should().Be(placed.Reference);
        db.ByToken(Token[..^1] + "b", db.SiteId).Should().BeNull();
        db.ByToken(Token, db.OtherSiteId).Should().BeNull("another store's order is not found, not forbidden");

        db.Scalar<int>("SELECT COUNT(*) FROM dbo.Purchase WHERE GuestTokenHash = CAST(@t AS varbinary(64));", ("@t", Token))
            .Should().Be(0, "the token itself is never stored");
    }

    [SkippableFact]
    public void ASignedInCustomerOrdersOnTheirOwnAccount()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var shirt = db.Product("SHIRT", price: 40m, stock: 3);
        var (account, contact) = db.Customer();
        var basket = db.Basket(contact);

        var placed = db.Place(basket, guest: false, (shirt, 1, 40m), contactId: contact);

        db.Row("SELECT AccountId, CAST(CASE WHEN GuestTokenHash IS NULL THEN 1 ELSE 0 END AS bit) FROM dbo.Purchase WHERE Reference = @ref;",
                ("@ref", placed.Reference))
            .Should().Equal(account, true);
        db.Scalar<int>("SELECT COUNT(*) FROM dbo.Account WHERE SiteId = @site;", ("@site", db.SiteId)).Should().Be(1);
        db.Scalar<int>("SELECT COUNT(*) FROM dbo.Basket WHERE Id = @b;", ("@b", basket)).Should().Be(0);
    }

    [SkippableFact]
    public void AnInclusiveStoreBacksTheTaxOutOfEachLine()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        db.Execute("UPDATE dbo.Site SET PricesIncludeTax = 1 WHERE Id = @site;", ("@site", db.SiteId));
        var shirt = db.Product("SHIRT", price: 99.90m, stock: 5, inclusive: true);

        var placed = db.Place(db.Basket(), guest: true, (shirt, 3, 99.90m));

        // 299.70 with 17% in it: 299.70 x 17 / 117 = 43.546..., and the total is what was shown.
        db.Row("SELECT SubTotal, VAT, FinalPrice FROM dbo.Purchase WHERE Reference = @ref;", ("@ref", placed.Reference))
            .Should().Equal(256.15m, 43.55m, 299.70m);
    }

    [SkippableFact]
    public void AShortOrderWritesNothingAndNamesWhatIsShort()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var shirt = db.Product("SHIRT", price: 40m, stock: 1);
        var socks = db.Product("SOCKS", price: 5m, stock: 9);
        var basket = db.Basket();

        var placed = db.Place(basket, guest: true, (shirt, 2, 40m), (socks, 1, 5m));

        placed.Reference.Should().BeNull();
        placed.ShortSkus.Should().Be(db.Sku("SHIRT"));
        db.Stock(shirt).Should().Be(1);
        db.Stock(socks).Should().Be(9, "a short order takes none of the stock, not just the short line's");
        db.Scalar<int>("SELECT COUNT(*) FROM dbo.Account WHERE SiteId = @site;", ("@site", db.SiteId)).Should().Be(0);
        db.Scalar<int>("SELECT COUNT(*) FROM dbo.Basket WHERE Id = @b;", ("@b", basket)).Should().Be(1);
    }

    [SkippableFact]
    public void AStyleIsNotSomethingAnOrderCanHold()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var style = db.Product("SHIRT", price: 40m, stock: 0);
        db.Execute("""
            DECLARE @options dbo.ProductOptionList;
            INSERT INTO @options (Name, Value, SortOrder) VALUES (N'Size', N'M', 0);
            EXEC dbo.spProduct_AddVariant @SiteId = @site, @ParentProductId = @style, @Sku = @sku,
                 @QuantityInStock = 3, @Options = @options;
            """, ("@site", db.SiteId), ("@style", style), ("@sku", db.Sku("SHIRT-M")));

        // Refused before the procedure opens its transaction, so this scope survives it.
        db.Invoking(d => d.Place(d.Basket(), guest: true, (style, 1, 40m)))
            .Should().Throw<SqlException>().Which.Number.Should().Be(50135);
    }

    [SkippableFact]
    public void CancellingACartOrderReturnsItsStockOnceAndItCannotBeReopened()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var shirt = db.Product("SHIRT", price: 40m, stock: 3);
        var placed = db.Place(db.Basket(), guest: true, (shirt, 2, 40m));
        var order = db.Scalar<int>("SELECT Id FROM dbo.Purchase WHERE Reference = @ref;", ("@ref", placed.Reference));

        db.SetStatus(order, "Cancelled");
        db.Stock(shirt).Should().Be(3);

        db.SetStatus(order, "Cancelled");
        db.Stock(shirt).Should().Be(3, "a second cancel has nothing left to return");

        // Last in the scope: the refusal is raised inside the procedure's transaction, and its
        // ROLLBACK unwinds this one too.
        db.Invoking(d => d.SetStatus(order, "Processing"))
            .Should().Throw<SqlException>().Which.Number.Should().Be(50136);
    }

    [SkippableFact]
    public void AConsumerRegistersApprovedUnderTheirOwnName()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);

        // Nothing to review: prepaid, no credit, no paperwork. A trade application stays Pending.
        db.Execute("""
            EXEC dbo.spAccount_Register @SiteId = @site, @IdentityUserId = N'user-c', @Company = NULL, @VatNumber = NULL,
                 @RegistrationNumber = NULL, @FirstName = N'Ana', @LastName = N'Silva', @Email = N'ana@example.invalid',
                 @Phone = NULL, @Line1 = NULL, @Line2 = NULL, @City = NULL, @Region = NULL, @PostCode = NULL, @Country = NULL,
                 @Kind = N'Consumer';
            """, ("@site", db.SiteId));

        db.Row("SELECT Kind, Status, Company FROM dbo.Account WHERE SiteId = @site;", ("@site", db.SiteId))
            .Should().Equal("Consumer", "Approved", "Ana Silva");
    }

    [SkippableFact]
    public void DeliveryIsTaxedWithTheGoodsAndInTheTotals()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var shirt = db.Product("SHIRT", price: 40m, stock: 3);

        var placed = db.PlaceWithDelivery(db.Basket(), (shirt, 2, 40m), "Standard", 10m);

        // 80 of goods and 10 of delivery, net, both at 17%: 13.60 and 1.70 of tax (T13).
        db.Row("SELECT ShippingName, ShippingPrice, ShippingVat, SubTotal, VAT, FinalPrice FROM dbo.Purchase WHERE Reference = @ref;",
                ("@ref", placed.Reference))
            .Should().Equal("Standard", 10m, 1.70m, 90m, 15.30m, 105.30m);
    }

    [SkippableFact]
    public void APaidEventPaysTheOrderOnceAndTellsTheCustomer()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var shirt = db.Product("SHIRT", price: 40m, stock: 3);
        var placed = db.Place(db.Basket(), guest: true, (shirt, 1, 40m));
        db.Session(placed.Reference!, "cs_1");

        db.Apply("evt_1", placed.Reference!, "cs_1", "Paid").Should().Be("Paid");
        db.Apply("evt_1", placed.Reference!, "cs_1", "Paid").Should().Be("Duplicate", "a replayed event changes nothing");

        db.Row("SELECT Status, CAST(CASE WHEN PaidUtc IS NULL THEN 0 ELSE 1 END AS bit) FROM dbo.Purchase WHERE Reference = @ref;",
                ("@ref", placed.Reference))
            .Should().Equal("Paid", true);
        db.Mailed("order.paid").Should().ContainSingle().Which.Should().Contain(placed.Reference);
    }

    [SkippableFact]
    public void OnlyTheOrdersCurrentSessionCanCancelIt()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var shirt = db.Product("SHIRT", price: 40m, stock: 3);
        var placed = db.Place(db.Basket(), guest: true, (shirt, 2, 40m));

        db.Session(placed.Reference!, "cs_old").Should().BeNull();
        db.Session(placed.Reference!, "cs_new").Should().Be("cs_old", "the caller expires the one it replaces");

        // "Pay now" expired the old session; its expiry must not cancel an order still being paid for.
        db.Apply("evt_old", placed.Reference!, "cs_old", "Failed").Should().Be("Unchanged");
        db.Stock(shirt).Should().Be(1);

        db.Apply("evt_new", placed.Reference!, "cs_new", "Failed").Should().Be("Cancelled");
        db.Stock(shirt).Should().Be(3, "an expired checkout gives its stock back");
        db.Session(placed.Reference!, "cs_later").Should().BeNull("a cancelled order takes no new session");
    }

    [SkippableFact]
    public void ACartOrderShippedWithATrackingNumberTellsTheCustomer()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var shirt = db.Product("SHIRT", price: 40m, stock: 3);
        var placed = db.Place(db.Basket(), guest: true, (shirt, 1, 40m));
        var order = db.Scalar<int>("SELECT Id FROM dbo.Purchase WHERE Reference = @ref;", ("@ref", placed.Reference));

        db.Execute("EXEC dbo.spOrder_UpdateStatus @Id = @id, @Status = N'Fulfilled', @SiteId = @site, @TrackingNumber = N' BR123 ';",
            ("@id", order), ("@site", db.SiteId));

        db.Scalar<string>("SELECT TrackingNumber FROM dbo.Purchase WHERE Id = @id;", ("@id", order)).Should().Be("BR123");

        // The procedure's JSON, read back through the record the renderer reads it with.
        var payload = System.Text.Json.JsonSerializer.Deserialize<SMDataManager.Library.Email.OrderShippedPayload>(
            db.Mailed("order.shipped").Single(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        payload.Should().Be(new SMDataManager.Library.Email.OrderShippedPayload(placed.Reference!, "BR123"));
    }

    [SkippableFact]
    public void ACheckoutStoreCannotOpenWithoutPaymentDeliveryAndAReturnsPage()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        db.Execute("UPDATE dbo.Site SET IsActive = 0 WHERE Id = @site;", ("@site", db.SiteId));

        // Raised before anything is written, so this scope survives it.
        db.Invoking(d => d.Execute("EXEC dbo.spSite_SetActive @SiteId = @site, @IsActive = 1;", ("@site", d.SiteId)))
            .Should().Throw<SqlException>().Which.Message
            .Should().Contain("its payment keys").And.Contain("a delivery rate").And.Contain("\"returns\" page");

        db.Invoking(d => d.Execute("EXEC dbo.spSite_SetPaymentSecrets @SiteId = @site, @SecretRef = N'x', @WebhookSecretRef = NULL;",
                ("@site", d.SiteId)))
            .Should().Throw<SqlException>().Which.Number.Should().Be(50140, "a key with no webhook secret takes money and never hears of it");
    }

    private sealed record Placed(string? Reference, string? ShortSkus);

    private sealed class Scenario : IDisposable
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly string _runId = Guid.NewGuid().ToString("N")[..10];

        private Scenario(SqlConnection connection, SqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;

            SiteId = NewSite("a");
            OtherSiteId = NewSite("b");

            Execute("""
                INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive) VALUES (@site, @slug, N'Shirts', 1, 1);
                INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId) VALUES (@site, @feed, SCOPE_IDENTITY());
                """, ("@site", SiteId), ("@slug", $"chk-{_runId}"), ("@feed", Category));
        }

        public static Scenario Open(out Scenario scenario)
        {
            var (connection, transaction) = TestDatabase.OpenRollbackScope();
            scenario = new Scenario(connection, transaction);
            return scenario;
        }

        public int SiteId { get; }
        public int OtherSiteId { get; }
        private string Category => $"Checkout-{_runId}";

        public string Sku(string code) => $"{code}-{_runId}";

        private int NewSite(string suffix) => Scalar<int>("""
            INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale, IsActive, OrderMode, StandardTaxRatePct)
            OUTPUT INSERTED.Id
            VALUES (@key, N'Checkout store', @domain, N'BR', N'EUR', N'pt-BR', 1, N'DirectCheckout', 17);
            """, ("@key", $"chk-{suffix}-{_runId}"), ("@domain", $"{suffix}.{_runId}.chk.invalid"));

        public int Product(string code, decimal price, int stock, bool inclusive = false) => Scalar<int>("""
            INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category, QuantityInStock, Delisted, [Source], CurrencyCode, IsTaxable, PricesIncludeTax)
            OUTPUT INSERTED.Id
            VALUES (N'Linen shirt', N'A shirt.', @price, @sku, @category, @stock, 0, N'Own', N'EUR', 1, @inclusive);
            """, ("@price", price), ("@sku", Sku(code)), ("@category", Category), ("@stock", stock), ("@inclusive", inclusive));

        public (int Account, int Contact) Customer()
        {
            var account = Scalar<int>("""
                INSERT INTO dbo.Account (Reference, Company, Currency, Status, SiteId)
                OUTPUT INSERTED.Id VALUES (@ref, N'Acme', N'EUR', N'Approved', @site);
                """, ("@ref", $"AC-{_runId}"), ("@site", SiteId));
            var contact = Scalar<int>("""
                INSERT INTO dbo.Contact (AccountId, IdentityUserId, FirstName, LastName, Email, IsPrimary)
                OUTPUT INSERTED.Id VALUES (@account, N'user-1', N'Bea', N'Costa', N'bea@example.invalid', 1);
                """, ("@account", account));
            return (account, contact);
        }

        public int Basket(int? contactId = null) => Scalar<int>(
            "INSERT INTO dbo.Basket (SiteId, ContactId, Token) OUTPUT INSERTED.Id VALUES (@site, @contact, @token);",
            ("@site", SiteId), ("@contact", contactId), ("@token", Guid.NewGuid().ToString("N")));

        public Placed Place(int basketId, bool guest, params (int ProductId, int Quantity, decimal Price)[] lines) =>
            Place(basketId, guest, lines, contactId: null);

        public Placed Place(int basketId, bool guest, (int ProductId, int Quantity, decimal Price) line, int contactId) =>
            Place(basketId, guest, [line], contactId);

        /// <summary>A guest's order of one line, with a delivery charge (T13).</summary>
        public Placed PlaceWithDelivery(int basketId, (int ProductId, int Quantity, decimal Price) line, string delivery, decimal price) =>
            Place(basketId, guest: true, [line], contactId: null, delivery, price);

        private Placed Place(int basketId, bool guest, (int ProductId, int Quantity, decimal Price)[] lines, int? contactId,
            string? delivery = null, decimal deliveryPrice = 0m)
        {
            var values = string.Join(", ", lines.Select((_, i) => $"(@p{i}, @q{i}, @n{i}, 0, @n{i})"));
            var parameters = new List<(string, object?)>
            {
                ("@site", SiteId), ("@basket", basketId), ("@contact", contactId),
                ("@token", guest ? Token : null), ("@sn", delivery), ("@sp", deliveryPrice)
            };
            for (var i = 0; i < lines.Length; i++)
            {
                parameters.Add(($"@p{i}", lines[i].ProductId));
                parameters.Add(($"@q{i}", lines[i].Quantity));
                parameters.Add(($"@n{i}", lines[i].Price));
            }

            using var command = Command($"""
                DECLARE @lines dbo.QuoteRequestLine;
                INSERT INTO @lines (ProductId, Quantity, ListPrice, DiscountPct, NetPrice) VALUES {values};
                EXEC dbo.spOrder_PlaceFromBasket @SiteId = @site, @BasketId = @basket, @Lines = @lines,
                     @ContactId = @contact, @Email = N'ana@example.invalid', @FirstName = N'Ana', @LastName = N'Silva',
                     @Line1 = N'Rua A, 10', @City = N'São Paulo', @Country = N'br',
                     @TaxTreatment = N'Domestic', @TaxLegend = N'Tax included.', @TaxRatePct = 17,
                     @GuestToken = @token, @ShippingName = @sn, @ShippingPrice = @sp;
                """, parameters.ToArray());
            using var reader = command.ExecuteReader();
            reader.Read();

            return new Placed(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1));
        }

        public string? ByToken(string token, int siteId)
        {
            using var command = Command("EXEC dbo.spOrder_GetByGuestToken @Token = @t, @SiteId = @site;",
                ("@t", token), ("@site", siteId));
            using var reader = command.ExecuteReader();
            return reader.Read() ? reader.GetString(reader.GetOrdinal("Reference")) : null;
        }

        public void SetStatus(int orderId, string status) => Execute(
            "EXEC dbo.spOrder_UpdateStatus @Id = @id, @Status = @status, @SiteId = @site;",
            ("@id", orderId), ("@status", status), ("@site", SiteId));

        public int Stock(int id) => Scalar<int>("SELECT QuantityInStock FROM dbo.Product WHERE Id = @id;", ("@id", id));

        /// <summary>Records a session on the order (T13), returning the one it replaced.</summary>
        public string? Session(string reference, string sessionId)
        {
            using var command = Command("EXEC dbo.spOrder_SetPaymentSession @SiteId = @site, @Reference = @ref, @SessionId = @s;",
                ("@site", SiteId), ("@ref", reference), ("@s", sessionId));
            using var reader = command.ExecuteReader();
            return reader.Read() && !reader.IsDBNull(0) ? reader.GetString(0) : null;
        }

        public string Apply(string eventId, string reference, string sessionId, string outcome) => Scalar<string>(
            "EXEC dbo.spOrder_ApplyPaymentEvent @SiteId = @site, @EventId = @e, @EventType = N'test', @Reference = @ref, @SessionId = @s, @Outcome = @o;",
            ("@site", SiteId), ("@e", $"{eventId}-{_runId}"), ("@ref", reference), ("@s", sessionId), ("@o", outcome));

        /// <summary>The payloads this store queued under a template.</summary>
        public List<string> Mailed(string templateKey)
        {
            using var command = Command("SELECT PayloadJson FROM dbo.EmailOutbox WHERE SiteId = @site AND TemplateKey = @k;",
                ("@site", SiteId), ("@k", templateKey));
            using var reader = command.ExecuteReader();
            var payloads = new List<string>();
            while (reader.Read()) payloads.Add(reader.GetString(0));
            return payloads;
        }

        public object[] Row(string sql, params (string Name, object? Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            using var reader = command.ExecuteReader();
            reader.Read().Should().BeTrue();
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            return values;
        }

        public void Execute(string sql, params (string Name, object? Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            command.ExecuteNonQuery();
        }

        public T Scalar<T>(string sql, params (string Name, object? Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            return (T)command.ExecuteScalar()!;
        }

        private SqlCommand Command(string sql, params (string Name, object? Value)[] parameters)
        {
            var command = new SqlCommand(sql, _connection, _transaction);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return command;
        }

        public void Dispose()
        {
            _transaction.Dispose();
            _connection.Dispose();
        }
    }
}
