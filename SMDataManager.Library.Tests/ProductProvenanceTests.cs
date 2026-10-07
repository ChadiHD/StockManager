using System.Data;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// A product belongs to the feed that imported it, and its prices are in a currency (T9).
/// </summary>
/// <remarks>
/// Database tests, because every rule here is T-SQL: the feed merge and its delisting, which
/// until now no test had run against a database at all, and dbo.fnSite_ProductSellable, which
/// the catalog, the admin screens and the quote submit all read. What they guard against is
/// quiet: an Irish distributor's notebooks on a UK store with a pound sign on euro prices, a
/// renamed feed whose products stay on sale at their last price for ever, and two stores' feeds
/// of the same name delisting each other's stock. None of those raises an error.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class ProductProvenanceTests
{
    [SkippableFact]
    public void AFeedMergesAndDelistsItsOwnProductsAndARenameKeepsThem()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new Db(connection, transaction);
            var store = db.Site("EUR");
            var feed = db.Feed(store, $"Feed {db.RunId}");

            db.Upsert(feed, $"Feed {db.RunId}", "A1", "A2");
            var first = db.ProductId(feed, "A1");

            // Renamed, as an operator might, and the file no longer lists A2.
            db.Execute("UPDATE dbo.DistributorFeed SET Name = @name WHERE Id = @feed;",
                ("@name", $"Renamed {db.RunId}"), ("@feed", feed));
            db.Upsert(feed, $"Renamed {db.RunId}", "A1");

            db.ProductId(feed, "A1").Should().Be(first, "a rename is the same feed, not a new catalog");
            db.Scalar<int>("SELECT COUNT(*) FROM dbo.Product WHERE FeedId = @feed;", ("@feed", feed)).Should().Be(2);
            db.Scalar<string>("SELECT Distributor FROM dbo.Product WHERE Id = @id;", ("@id", first))
                .Should().Be($"Renamed {db.RunId}", "brand aliases and ExcludeDistributor read the current name");
            db.Scalar<bool>("SELECT Delisted FROM dbo.Product WHERE FeedId = @feed AND DistributorSku = @sku;",
                ("@feed", feed), ("@sku", $"A2-{db.RunId}")).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void TwoStoresFeedsOfTheSameNameDoNotDelistEachOthersStock()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new Db(connection, transaction);
            var name = $"Main {db.RunId}";
            var feedA = db.Feed(db.Site("EUR"), name);
            var feedB = db.Feed(db.Site("EUR"), name);

            db.Upsert(feedA, name, "S1");
            db.Upsert(feedB, name, "S2");

            // B's file never listed S1. Merged on the name, B's sync delisted it.
            db.Scalar<bool>("SELECT Delisted FROM dbo.Product WHERE FeedId = @feed;", ("@feed", feedA)).Should().BeFalse();
            db.Scalar<int>("SELECT COUNT(*) FROM dbo.Product WHERE FeedId IN (@a, @b);", ("@a", feedA), ("@b", feedB)).Should().Be(2);
        }
    }

    [SkippableFact]
    public void EachStoreSellsOnlyItsOwnFeedsStockInItsOwnCurrency()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new Db(connection, transaction);
            var irish = db.Site("EUR");
            var uk = db.Site("GBP");
            var irishFeed = db.Feed(irish, $"IE {db.RunId}");
            var ukFeed = db.Feed(uk, $"UK {db.RunId}");

            // Both distributors file under the same category name, and both stores map it: the
            // case that sold the Irish distributor's stock on the UK store.
            db.Upsert(irishFeed, $"IE {db.RunId}", "IE1");
            db.Upsert(ukFeed, $"UK {db.RunId}", "UK1");
            db.MapCategory(irish);
            db.MapCategory(uk);

            db.CatalogSkus(irish).Should().Equal($"IE1-{db.RunId}");
            db.CatalogSkus(uk).Should().Equal($"UK1-{db.RunId}");

            db.Scalar<string>("SELECT CurrencyCode FROM dbo.Product WHERE FeedId = @feed;", ("@feed", ukFeed))
                .Should().Be("GBP", "a feed's products are priced in its store's currency");

            // And the admin's screens see what the shop sees.
            db.AdminCatalogSkus(uk).Should().Equal($"UK1-{db.RunId}");
            db.FeedCategories(uk).Should().Equal(db.Category);
        }
    }

    [SkippableFact]
    public void OwnStockIsSoldByEveryStoreInItsCurrencyAndAnOrphanByNone()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new Db(connection, transaction);
            var euroA = db.Site("EUR");
            var euroB = db.Site("EUR");
            var pounds = db.Site("GBP");

            db.Insert("OWN", source: "Own", currency: "EUR");
            // A distributor product whose feed is gone, or that predates FeedId with a name two
            // feeds shared.
            db.Insert("ORPHAN", source: "Distributor", currency: "EUR");

            foreach (var site in new[] { euroA, euroB, pounds }) db.MapCategory(site);

            db.CatalogSkus(euroA).Should().Equal($"OWN-{db.RunId}");
            db.CatalogSkus(euroB).Should().Equal($"OWN-{db.RunId}");
            db.CatalogSkus(pounds).Should().BeEmpty();
        }
    }

    [SkippableFact]
    public void AProductRelabelledOwnStaysItsFeedsStoresAlone()
    {
        // spProduct_Update lets an admin rewrite Source. Keyed on Source, an imported product
        // relabelled "Own" would go on sale in every store in its currency.
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new Db(connection, transaction);
            var owner = db.Site("EUR");
            var other = db.Site("EUR");
            var feed = db.Feed(owner, $"F {db.RunId}");
            db.Upsert(feed, $"F {db.RunId}", "P1");
            db.Execute("UPDATE dbo.Product SET Source = N'Own' WHERE FeedId = @feed;", ("@feed", feed));
            db.MapCategory(owner);
            db.MapCategory(other);

            db.CatalogSkus(owner).Should().Equal($"P1-{db.RunId}");
            db.CatalogSkus(other).Should().BeEmpty();
        }
    }

    [SkippableFact]
    public void AccountsQuotesAndOrdersTakeTheirStoresCurrencyNotTheCallers()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new Db(connection, transaction);
            var uk = db.Site("GBP");

            db.Execute("""
                DECLARE @id int;
                EXEC dbo.spAccount_Insert @Id = @id OUTPUT, @Company = N'Pounds Ltd', @ContactName = N'P',
                     @Email = N'p@example.test', @Country = N'GB', @CustomerGroupId = NULL,
                     @PaymentMethod = N'Card', @PaymentTerms = N'Prepaid', @CreditLimit = 0,
                     @Status = N'Approved', @SiteId = @site;
                """, ("@site", uk));
            var account = db.Scalar<int>("SELECT Id FROM dbo.Account WHERE SiteId = @site;", ("@site", uk));

            // An order records exactly one placer (CK_Purchase_Placer), and this one is staff.
            var staff = $"prov-staff-{db.RunId}";
            db.Execute("INSERT INTO dbo.[User] (UserId, FirstName, LastName, EmailAddress) VALUES (@id, N'P', N'S', N'ps@example.test');",
                ("@id", staff));

            db.Execute("""
                DECLARE @id int, @ref nvarchar(20);
                EXEC dbo.spQuote_Insert @Id = @id OUTPUT, @Reference = @ref OUTPUT, @AccountId = @account,
                     @ExpiresDate = NULL, @SiteId = @site;
                EXEC dbo.spOrder_Insert @Id = @id OUTPUT, @Reference = @ref OUTPUT, @StaffId = @staff,
                     @AccountId = @account, @SiteId = @site;
                """, ("@site", uk), ("@account", account), ("@staff", staff));

            db.Scalar<string>("SELECT Currency FROM dbo.Account WHERE Id = @a;", ("@a", account)).Should().Be("GBP");
            db.Scalar<string>("SELECT Currency FROM dbo.Quote WHERE AccountId = @a;", ("@a", account)).Should().Be("GBP");
            db.Scalar<string>("SELECT Currency FROM dbo.Purchase WHERE AccountId = @a;", ("@a", account)).Should().Be("GBP");
        }
    }

    private sealed class Db(SqlConnection connection, SqlTransaction transaction)
    {
        // SiteKey and Domain are unique, and a rolled-back transaction still collides with a
        // concurrent one that has not rolled back yet.
        public string RunId { get; } = Guid.NewGuid().ToString("N")[..10];

        public string Category => $"Provenance-{RunId}";

        private int _sites;

        public int Site(string currency)
        {
            var n = ++_sites;
            return Scalar<int>("""
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Provenance store', @domain, N'IE', @currency, N'en-IE', 1);
                """, ("@key", $"prov-{RunId}-{n}"), ("@domain", $"{RunId}-{n}.prov.invalid"), ("@currency", currency));
        }

        public int Feed(int siteId, string name) => Scalar<int>("""
            INSERT INTO dbo.DistributorFeed (Name, Host, Username, SiteId)
            OUTPUT INSERTED.Id VALUES (@name, N'sftp.prov.invalid', N'fixture', @site);
            """, ("@name", name), ("@site", siteId));

        public void Upsert(int feedId, string distributor, params string[] skus)
        {
            var items = new DataTable();
            foreach (var column in new[] { "DistributorSku", "Sku", "ProductName", "Description", "Category" })
                items.Columns.Add(column, typeof(string));
            items.Columns.Add("Cost", typeof(decimal));
            items.Columns.Add("Srp", typeof(decimal));
            items.Columns.Add("QuantityInStock", typeof(int));
            foreach (var column in new[] { "Manufacturer", "ManufacturerPartNumber", "Ean" })
                items.Columns.Add(column, typeof(string));
            items.Columns.Add("IcecatAvailable", typeof(bool));

            foreach (var sku in skus)
            {
                var code = $"{sku}-{RunId}";
                items.Rows.Add(code, code, $"Product {code}", null, Category, 50m, 100m, 5, null, null, null, null);
            }

            using var command = new SqlCommand("dbo.spProduct_BulkUpsertFromFeed", connection, transaction)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.AddWithValue("@FeedId", feedId);
            command.Parameters.AddWithValue("@Distributor", distributor);
            var tvp = command.Parameters.AddWithValue("@Items", items);
            tvp.SqlDbType = SqlDbType.Structured;
            tvp.TypeName = "dbo.DistributorFeedItem";

            using var reader = command.ExecuteReader();
        }

        public void Insert(string sku, string source, string currency) => Execute("""
            INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category, QuantityInStock,
                                     Delisted, [Source], CurrencyCode)
            VALUES (@name, N'Provenance fixture.', 100, @sku, @category, 5, 0, @source, @currency);
            """, ("@name", $"Product {sku}"), ("@sku", $"{sku}-{RunId}"), ("@category", Category),
            ("@source", source), ("@currency", currency));

        public int ProductId(int feedId, string sku) => Scalar<int>(
            "SELECT Id FROM dbo.Product WHERE FeedId = @feed AND DistributorSku = @sku;",
            ("@feed", feedId), ("@sku", $"{sku}-{RunId}"));

        public void MapCategory(int siteId) => Execute("""
            INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive) VALUES (@site, @slug, N'Prov', 1, 1);
            INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId) VALUES (@site, @feed, SCOPE_IDENTITY());
            """, ("@site", siteId), ("@slug", $"prov-{RunId}"), ("@feed", Category));

        // The storefront's listing, narrowed to this test's category.
        public List<string> CatalogSkus(int siteId) => Strings(
            "EXEC dbo.spCatalog_Search @SiteId = @site, @CategorySlug = @slug, @PageSize = 100;", "Sku",
            ("@site", siteId), ("@slug", $"prov-{RunId}"));

        public List<string> AdminCatalogSkus(int siteId) => Strings(
            "EXEC dbo.spProduct_GetCatalogForSite @SiteId = @site;", "Sku", ("@site", siteId))
            .Where(sku => sku.EndsWith(RunId)).ToList();

        public List<string> FeedCategories(int siteId) => Strings(
            "EXEC dbo.spCategoryMapping_GetForSite @SiteId = @site;", "FeedValue", ("@site", siteId))
            .Where(value => value.EndsWith(RunId)).ToList();

        private List<string> Strings(string sql, string column, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            using var reader = command.ExecuteReader();

            var values = new List<string>();
            while (reader.Read()) values.Add(reader.GetString(reader.GetOrdinal(column)));

            return values;
        }

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

        private SqlCommand Command(string sql, (string Name, object Value)[] parameters)
        {
            var command = new SqlCommand(sql, connection, transaction);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return command;
        }
    }
}
