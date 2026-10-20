using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// A style and its sizes (T11), through the procedures the storefront and the portal call.
/// </summary>
/// <remarks>
/// Every claim here is about a row the catalogue function returns or does not, and none of it
/// has a failure mode that looks like an error: list the variants beside their style and the
/// shop shows one shirt four times; place them by their own id and hiding the shirt leaves its
/// sizes on sale.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class VariantCatalogTests
{
    [SkippableFact]
    public void TheListingShowsAStyleOnceWithItsVariantsStock()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var style = db.Style("SHIRT", price: 40m);
        db.Variant(style, "SHIRT-M", 3, ("Size", "M"), ("Colour", "Navy"));
        db.Variant(style, "SHIRT-L", 0, ("Size", "L"), ("Colour", "Navy"));
        db.Plain("SOCKS", stock: 2);

        var rows = db.Listing(inStockOnly: false);

        rows.Select(row => row.Sku).Should().BeEquivalentTo(db.Skus("SHIRT", "SOCKS"));
        var shirt = rows.Single(row => row.Sku == db.Sku("SHIRT"));
        shirt.Stock.Should().Be(3, "a style's stock is its live variants' together");
        shirt.HasVariants.Should().BeTrue();
        rows.Single(row => row.Sku == db.Sku("SOCKS")).HasVariants.Should().BeFalse();
    }

    [SkippableFact]
    public void InStockOnlyMeansSomeSizeIsInStock()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var style = db.Style("SHIRT", price: 40m);
        var medium = db.Variant(style, "SHIRT-M", 0, ("Size", "M"));
        db.Variant(style, "SHIRT-L", 0, ("Size", "L"));

        db.Listing(inStockOnly: true).Should().BeEmpty();

        db.Execute("UPDATE dbo.Product SET QuantityInStock = 2 WHERE Id = @id;", ("@id", medium));

        db.Listing(inStockOnly: true).Select(row => row.Sku).Should().Equal(db.Sku("SHIRT"));
    }

    [SkippableFact]
    public void AStyleCannotGoInABasketButItsVariantCan()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var style = db.Style("SHIRT", price: 40m);
        var medium = db.Variant(style, "SHIRT-M", 3, ("Size", "M"));
        var basket = db.Basket();

        // Nothing to ship until a size is chosen. The refusal is raised before the procedure
        // opens its transaction, so this scope survives it.
        db.Invoking(d => d.AddLine(basket, style)).Should().Throw<SqlException>()
            .Which.Number.Should().Be(50021);

        db.AddLine(basket, medium);
        db.Scalar<int>("SELECT COUNT(*) FROM dbo.BasketLine WHERE BasketId = @b;", ("@b", basket)).Should().Be(1);
    }

    [SkippableFact]
    public void HidingTheStyleOnAStoreHidesEveryVariantThere()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var style = db.Style("SHIRT", price: 40m);
        db.Variant(style, "SHIRT-M", 3, ("Size", "M"));

        db.BySku("SHIRT-M").Should().BeTrue();

        // One SiteProduct row, for the style. A store hides a shirt, not its size M.
        db.Execute("INSERT INTO dbo.SiteProduct (SiteId, ProductId, Visibility) VALUES (@site, @style, N'Hide');",
            ("@site", db.SiteId), ("@style", style));

        db.BySku("SHIRT-M").Should().BeFalse();
        db.BySku("SHIRT").Should().BeFalse();
    }

    [SkippableFact]
    public void AVariantIsNamedForItsOptionsAndFollowsItsStyle()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var style = db.Style("SHIRT", price: 40m);
        var medium = db.Variant(style, "SHIRT-M", 3, ("Size", "M"), ("Colour", "Navy"));

        db.Name(medium).Should().Be("Linen shirt — M / Navy",
            "the basket, the quote and the order all show the name, so it has to say which size");

        // A style renamed and repriced is renamed and repriced in every size.
        db.Update(style, name: "Oxford shirt", price: 45m, stock: 0);
        db.Name(medium).Should().Be("Oxford shirt — M / Navy");
        db.Price(medium).Should().Be(45m);
        db.Stock(medium).Should().Be(3, "stock is the variant's own");

        // A variant priced apart from its style would list one price and charge another, so a
        // variant's save writes its stock and nothing else.
        db.Update(medium, name: "Cheap", price: 1m, stock: 7);
        db.Name(medium).Should().Be("Oxford shirt — M / Navy");
        db.Price(medium).Should().Be(45m);
        db.Stock(medium).Should().Be(7);
    }

    [SkippableFact]
    public void AVariantMustBeTellableFromItsSiblingsAndCannotHaveVariantsOfItsOwn()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var style = db.Style("SHIRT", price: 40m);
        var medium = db.Variant(style, "SHIRT-M", 3, ("Size", "M"), ("Colour", "Navy"));

        // Each refusal is raised before the procedure opens its transaction.
        db.Invoking(d => d.Variant(style, "SHIRT-M2", 1, ("Colour", "Navy"), ("Size", "M")))
            .Should().Throw<SqlException>().Which.Number.Should().Be(50123);
        db.Invoking(d => d.Variant(medium, "SHIRT-M-X", 1, ("Fit", "Slim")))
            .Should().Throw<SqlException>().Which.Number.Should().Be(50121);
        db.Invoking(d => d.Variant(style, "SHIRT-NONE", 1))
            .Should().Throw<SqlException>().Which.Number.Should().Be(50122);
    }

    [SkippableFact]
    public void AVariantSomebodyHoldsCannotBeRemoved()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var style = db.Style("SHIRT", price: 40m);
        var medium = db.Variant(style, "SHIRT-M", 3, ("Size", "M"));
        var large = db.Variant(style, "SHIRT-L", 3, ("Size", "L"));
        db.AddLine(db.Basket(), medium);

        db.Invoking(d => d.Remove(medium)).Should().Throw<SqlException>().Which.Number.Should().Be(50125);

        db.Remove(large);
        db.Scalar<int>("SELECT COUNT(*) FROM dbo.Product WHERE ParentProductId = @s;", ("@s", style)).Should().Be(1);
    }

    [SkippableFact]
    public void AStylesFamilyIsTheStyleFirstThenItsVariantsAndTheirOptions()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var style = db.Style("SHIRT", price: 40m);
        var medium = db.Variant(style, "SHIRT-M", 3, ("Size", "M"), ("Colour", "Navy"));
        var large = db.Variant(style, "SHIRT-L", 0, ("Size", "L"), ("Colour", "Navy"));

        var (ids, options) = db.Family(style);

        ids.Should().Equal(style, medium, large);
        options.Should().HaveCount(4);
    }

    [SkippableFact]
    public void AnOptionFilterMatchesOneVariantCarryingEverySelectedName()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var oxford = db.Style("OXFORD", price: 40m);
        db.Variant(oxford, "OXFORD-M-NAVY", 2, ("Size", "M"), ("Colour", "Navy"));
        db.Variant(oxford, "OXFORD-L-WHITE", 2, ("Size", "L"), ("Colour", "White"));
        var linen = db.Style("LINEN", price: 30m);
        db.Variant(linen, "LINEN-M-WHITE", 2, ("Size", "M"), ("Colour", "White"));

        // Navy and M on one shirt, not M on one and Navy on another: only the oxford has an M that
        // is Navy, though the linen shirt has an M and the oxford has a white one.
        db.Listing(false, ("Size", "M"), ("Colour", "Navy")).Select(row => row.Sku).Should().Equal(db.Sku("OXFORD"));

        // Two values for one name mean either.
        db.Listing(false, ("Size", "M"), ("Size", "L"), ("Colour", "White"))
            .Select(row => row.Sku).Should().BeEquivalentTo(db.Skus("OXFORD", "LINEN"));

        // A plain product has no variant to match, so it is not what anybody filtering by size wants.
        db.Plain("SOCKS", stock: 2);
        db.Listing(false, ("Size", "M")).Select(row => row.Sku).Should().BeEquivalentTo(db.Skus("OXFORD", "LINEN"));
    }

    [SkippableFact]
    public void TheOptionRailCountsStylesNotVariantsAndAgreesWithThePage()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        using var scope = Scenario.Open(out var db);
        var oxford = db.Style("OXFORD", price: 40m);
        db.Variant(oxford, "OXFORD-M-NAVY", 2, ("Size", "M"), ("Colour", "Navy"));
        db.Variant(oxford, "OXFORD-M-WHITE", 2, ("Size", "M"), ("Colour", "White"));
        var linen = db.Style("LINEN", price: 30m);
        db.Variant(linen, "LINEN-M-WHITE", 2, ("Size", "M"), ("Colour", "White"));
        db.Variant(linen, "LINEN-S-WHITE", 0, ("Size", "S"), ("Colour", "White"));

        var rail = db.OptionRail(inStockOnly: false);

        // Two shirts come in M, though three variants do; the rail counts what the page lists.
        rail.Should().Contain(("Size", "M", 2));
        rail.Should().Contain(("Colour", "White", 2));
        rail.Should().Contain(("Size", "S", 1));
        rail.Count(entry => entry.Name == "Size" && entry.Value == "M").Should().Be(1);

        // In-stock-only takes the out-of-stock S off the rail as it takes it off the page.
        db.OptionRail(inStockOnly: true).Should().NotContain(entry => entry.Value == "S");
        db.Listing(true, ("Size", "S")).Should().BeEmpty();
    }

    private sealed record ListingRow(string Sku, int Stock, bool HasVariants);

    private sealed class Scenario : IDisposable
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly string _runId = Guid.NewGuid().ToString("N")[..10];

        private Scenario(SqlConnection connection, SqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;

            SiteId = Scalar<int>("""
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Variant store', @domain, N'IE', N'EUR', N'en-IE', 1);
                """, ("@key", $"var-{_runId}"), ("@domain", $"{_runId}.var.invalid"));

            Execute("""
                INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive) VALUES (@site, @slug, N'Shirts', 1, 1);
                INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId) VALUES (@site, @feed, SCOPE_IDENTITY());
                """, ("@site", SiteId), ("@slug", Slug), ("@feed", Category));
        }

        public static Scenario Open(out Scenario scenario)
        {
            var (connection, transaction) = TestDatabase.OpenRollbackScope();
            scenario = new Scenario(connection, transaction);
            return scenario;
        }

        public int SiteId { get; }
        private string Slug => $"var-{_runId}";
        private string Category => $"Variants-{_runId}";

        public string Sku(string code) => $"{code}-{_runId}";
        public string[] Skus(params string[] codes) => codes.Select(Sku).ToArray();

        public int Style(string code, decimal price) => Scalar<int>("""
            INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category, QuantityInStock, Delisted, [Source], CurrencyCode)
            OUTPUT INSERTED.Id
            VALUES (N'Linen shirt', N'A shirt.', @price, @sku, @category, 0, 0, N'Own', N'EUR');
            """, ("@price", price), ("@sku", Sku(code)), ("@category", Category));

        public int Plain(string code, int stock) => Scalar<int>("""
            INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category, QuantityInStock, Delisted, [Source], CurrencyCode)
            OUTPUT INSERTED.Id
            VALUES (N'Socks', N'Socks.', 5, @sku, @category, @stock, 0, N'Own', N'EUR');
            """, ("@sku", Sku(code)), ("@category", Category), ("@stock", stock));

        public int Variant(int styleId, string code, int stock, params (string Name, string Value)[] options)
        {
            var values = string.Join(", ", options.Select((_, i) => $"(@n{i}, @v{i}, {i})"));
            var insert = options.Length == 0 ? string.Empty : $"INSERT INTO @options (Name, Value, SortOrder) VALUES {values};";

            var parameters = new List<(string, object)>
            {
                ("@site", SiteId), ("@style", styleId), ("@sku", Sku(code)), ("@stock", stock)
            };
            for (var i = 0; i < options.Length; i++)
            {
                parameters.Add(($"@n{i}", options[i].Name));
                parameters.Add(($"@v{i}", options[i].Value));
            }

            return Scalar<int>($"""
                DECLARE @options dbo.ProductOptionList;
                {insert}
                EXEC dbo.spProduct_AddVariant @SiteId = @site, @ParentProductId = @style, @Sku = @sku,
                     @QuantityInStock = @stock, @Options = @options;
                """, parameters.ToArray());
        }

        public List<ListingRow> Listing(bool inStockOnly, params (string Name, string Value)[] options)
        {
            using var command = Command(
                $"""
                DECLARE @options dbo.ProductOptionList;
                {OptionInserts(options)}
                EXEC dbo.spCatalog_Search @SiteId = @site, @CategorySlug = @slug, @InStockOnly = @stock,
                     @PageSize = 96, @Options = @options;
                """,
                OptionParameters(options, ("@site", SiteId), ("@slug", Slug), ("@stock", inStockOnly)));
            using var reader = command.ExecuteReader();

            var rows = new List<ListingRow>();
            while (reader.Read())
            {
                rows.Add(new ListingRow(
                    reader.GetString(reader.GetOrdinal("Sku")),
                    reader.GetInt32(reader.GetOrdinal("QuantityInStock")),
                    reader.GetBoolean(reader.GetOrdinal("HasVariants"))));
            }

            return rows;
        }

        /// <summary>The third grid of spCatalog_GetFacets, within this scenario's category.</summary>
        public List<(string Name, string Value, int Count)> OptionRail(bool inStockOnly)
        {
            using var command = Command(
                "EXEC dbo.spCatalog_GetFacets @SiteId = @site, @CategorySlug = @slug, @InStockOnly = @stock;",
                ("@site", SiteId), ("@slug", Slug), ("@stock", inStockOnly));
            using var reader = command.ExecuteReader();

            reader.NextResult();
            reader.NextResult();

            var rail = new List<(string, string, int)>();
            while (reader.Read()) rail.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));

            return rail;
        }

        private static string OptionInserts((string Name, string Value)[] options) =>
            options.Length == 0
                ? string.Empty
                : "INSERT INTO @options (Name, Value) VALUES "
                  + string.Join(", ", options.Select((_, i) => $"(@n{i}, @v{i})")) + ";";

        private static (string, object)[] OptionParameters(
            (string Name, string Value)[] options, params (string, object)[] parameters) =>
            parameters
                .Concat(options.SelectMany((option, i) => new (string, object)[] { ($"@n{i}", option.Name), ($"@v{i}", option.Value) }))
                .ToArray();

        public bool BySku(string code)
        {
            using var command = Command("EXEC dbo.spCatalog_GetBySku @SiteId = @site, @Sku = @sku;",
                ("@site", SiteId), ("@sku", Sku(code)));
            using var reader = command.ExecuteReader();
            return reader.Read();
        }

        public int Basket() => Scalar<int>(
            "INSERT INTO dbo.Basket (SiteId, Token) OUTPUT INSERTED.Id VALUES (@site, @token);",
            ("@site", SiteId), ("@token", $"{_runId}-variant-basket-token-aaaaaaaaaaaaaaaa"[..43]));

        public void AddLine(int basketId, int productId) => Execute(
            "EXEC dbo.spBasket_AddLine @BasketId = @b, @SiteId = @site, @ProductId = @p, @Quantity = 1;",
            ("@b", basketId), ("@site", SiteId), ("@p", productId));

        public void Update(int id, string name, decimal price, int stock) => Execute("""
            EXEC dbo.spProduct_Update @Id = @id, @ProductName = @name, @Description = N'A shirt.', @Category = @category,
                 @Source = N'Own', @Distributor = NULL, @DistributorSku = NULL, @Cost = 10, @RetailPrice = @price,
                 @QuantityInStock = @stock, @IsTaxable = 1, @ProductImage = NULL, @Manufacturer = N'Acme';
            """, ("@id", id), ("@name", name), ("@category", Category), ("@price", price), ("@stock", stock));

        public void Remove(int id) => Execute("EXEC dbo.spProduct_RemoveVariant @SiteId = @site, @Id = @id;",
            ("@site", SiteId), ("@id", id));

        public (List<int> Ids, List<string> Options) Family(int styleId)
        {
            using var command = Command("EXEC dbo.spCatalog_GetFamily @SiteId = @site, @ProductId = @style;",
                ("@site", SiteId), ("@style", styleId));
            using var reader = command.ExecuteReader();

            var ids = new List<int>();
            while (reader.Read()) ids.Add(reader.GetInt32(reader.GetOrdinal("Id")));

            reader.NextResult();
            var options = new List<string>();
            while (reader.Read()) options.Add($"{reader.GetString(1)}={reader.GetString(2)}");

            return (ids, options);
        }

        public string Name(int id) => Scalar<string>("SELECT ProductName FROM dbo.Product WHERE Id = @id;", ("@id", id));
        public decimal Price(int id) => Scalar<decimal>("SELECT RetailPrice FROM dbo.Product WHERE Id = @id;", ("@id", id));
        public int Stock(int id) => Scalar<int>("SELECT QuantityInStock FROM dbo.Product WHERE Id = @id;", ("@id", id));

        public void Execute(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            command.ExecuteNonQuery();
        }

        public T Scalar<T>(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            return (T)command.ExecuteScalar()!;
        }

        private SqlCommand Command(string sql, params (string Name, object Value)[] parameters)
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
