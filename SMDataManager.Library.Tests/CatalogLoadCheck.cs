using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace SMDataManager.Library.Tests;

/// <summary>
/// T7's load check: the catalog procedures at 50,000 products and two stores.
/// </summary>
/// <remarks>
/// Opt-in, against a database of its own — <c>SMDATABASE_LOAD_CONNECTION</c>, never the
/// development one — because the seed commits: fifty thousand rows rolled back per run would
/// measure the rollback. Publish the DACPAC to an empty database first; the seed runs once and
/// is recognised by its site keys afterwards.
///
/// The shape is the one T8 §7 asked about: the default (featured) browse, a search, a
/// category, a price sort for a priced group, the facets and a product page — on a store with
/// two thousand per-product overrides, so <c>fnSite_ProductPlacement</c>'s override join is
/// doing real work. Fifty runs each after one to warm the plan; the 95th percentile has to be
/// under 100 ms. The portal's whole-catalog snapshot is reported, not asserted: whether it
/// holds at this size is the question, and the answer is a decision, not a test.
/// </remarks>
public class CatalogLoadCheck
{
    private const int Products = 50_000;
    private const int Runs = 50;
    private static readonly TimeSpan Target = TimeSpan.FromMilliseconds(100);

    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("SMDATABASE_LOAD_CONNECTION");

    private readonly ITestOutputHelper _output;

    public CatalogLoadCheck(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    [Trait("Category", "Load")]
    public void TheCatalogAnswersInsideTheTargetAtFiftyThousandProducts()
    {
        Skip.If(string.IsNullOrWhiteSpace(ConnectionString),
            "Set SMDATABASE_LOAD_CONNECTION to a database of its own, with the DACPAC published, to run the load check.");

        using var connection = new SqlConnection(ConnectionString);
        connection.Open();

        var (siteId, groupId) = Seed(connection);

        var cases = new (string Name, string Procedure, object Parameters)[]
        {
            ("browse, featured", "dbo.spCatalog_Search", new { SiteId = siteId, Sort = "featured" }),
            ("browse, a category", "dbo.spCatalog_Search", new { SiteId = siteId, CategorySlug = "load-cat-07" }),
            ("search, a word", "dbo.spCatalog_Search", new { SiteId = siteId, Search = "ultrabook" }),
            ("search, a SKU prefix", "dbo.spCatalog_Search", new { SiteId = siteId, Search = "LD-0123" }),
            ("price sort, priced group", "dbo.spCatalog_Search",
                new { SiteId = siteId, CustomerGroupId = groupId, Sort = "price-asc", InStockOnly = true }),
            ("deep page", "dbo.spCatalog_Search", new { SiteId = siteId, Page = 400 }),
            ("facets, unfiltered", "dbo.spCatalog_GetFacets", new { SiteId = siteId }),
            ("facets, searched", "dbo.spCatalog_GetFacets", new { SiteId = siteId, Search = "ultrabook" }),
            ("product page", "dbo.spCatalog_GetBySku", new { SiteId = siteId, Sku = "LD-012345" }),
        };

        var failures = new List<string>();

        foreach (var (name, procedure, parameters) in cases)
        {
            Execute(connection, procedure, parameters);

            var timings = Enumerable.Range(0, Runs)
                .Select(_ => Execute(connection, procedure, parameters).Elapsed)
                .OrderBy(elapsed => elapsed)
                .ToList();

            var p50 = timings[Runs / 2];
            var p95 = timings[(int)(Runs * 0.95) - 1];

            _output.WriteLine($"{name,-26} p50 {p50.TotalMilliseconds,7:F1} ms   p95 {p95.TotalMilliseconds,7:F1} ms");

            if (p95 > Target)
            {
                failures.Add($"{name}: p95 {p95.TotalMilliseconds:F1} ms");
            }
        }

        // The admin portal loads this whole list into the browser and deserialises it on the
        // WebAssembly thread. Reported for the decision T8 §7 deferred to here.
        var snapshot = Execute(connection, "dbo.spProduct_GetCatalogForSite", new { SiteId = siteId });
        _output.WriteLine(
            $"portal catalog snapshot    {snapshot.Rows,7} rows   {snapshot.JsonBytes / 1024.0 / 1024.0:F1} MB as JSON   " +
            $"{snapshot.Elapsed.TotalMilliseconds:F0} ms in SQL");

        failures.Should().BeEmpty($"each procedure's 95th percentile must be under {Target.TotalMilliseconds} ms");
    }

    private sealed record Result(TimeSpan Elapsed, int Rows, long JsonBytes);

    private static Result Execute(SqlConnection connection, string procedure, object parameters)
    {
        using var command = new SqlCommand(procedure, connection) { CommandType = System.Data.CommandType.StoredProcedure };

        foreach (var property in parameters.GetType().GetProperties())
        {
            command.Parameters.AddWithValue("@" + property.Name, property.GetValue(parameters) ?? DBNull.Value);
        }

        var clock = Stopwatch.StartNew();
        var rows = 0;
        long bytes = 0;

        using (var reader = command.ExecuteReader())
        {
            do
            {
                while (reader.Read())
                {
                    rows++;

                    if (procedure == "dbo.spProduct_GetCatalogForSite")
                    {
                        var row = new Dictionary<string, object?>();
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }

                        bytes += JsonSerializer.SerializeToUtf8Bytes(row).Length + 1;
                    }
                }
            }
            while (reader.NextResult());
        }

        return new Result(clock.Elapsed, rows, bytes);
    }

    /// <summary>
    /// Two stores, forty categories each over sixty feed categories (twenty shared), fifty
    /// thousand products across fifty brands, a priced group, and two thousand overrides on the
    /// measured store: a thousand hidden, five hundred shown from unmapped categories, five
    /// hundred featured. Once only; recognised by the site key.
    /// </summary>
    private (int SiteId, int GroupId) Seed(SqlConnection connection)
    {
        using (var check = new SqlCommand("SELECT Id FROM dbo.Site WHERE SiteKey = N'load-a'", connection))
        {
            if (check.ExecuteScalar() is int existing)
            {
                return (existing, Scalar(connection, $"SELECT Id FROM dbo.CustomerGroup WHERE SiteId = {existing}"));
            }
        }

        var clock = Stopwatch.StartNew();

        using (var seed = new SqlCommand(SeedSql, connection) { CommandTimeout = 600 })
        {
            seed.Parameters.AddWithValue("@Products", Products);
            seed.ExecuteNonQuery();
        }

        _output.WriteLine($"seeded {Products:N0} products in {clock.Elapsed.TotalSeconds:F0} s");

        var siteId = Scalar(connection, "SELECT Id FROM dbo.Site WHERE SiteKey = N'load-a'");

        return (siteId, Scalar(connection, $"SELECT Id FROM dbo.CustomerGroup WHERE SiteId = {siteId}"));
    }

    private static int Scalar(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private const string SeedSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale, OrderMode,
                              RegistrationFieldSet, PriceDisplay, MinMarginPct, FeedStaleAfterHours,
                              HideStaleProducts, IsActive)
        VALUES (N'load-a', N'Load store A', N'load-a.invalid', N'IE', N'EUR', N'en-IE', N'Rfq', N'eu-b2b', N'Public', 5, 48, 1, 1),
               (N'load-b', N'Load store B', N'load-b.invalid', N'IE', N'EUR', N'en-IE', N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);

        DECLARE @a int = (SELECT Id FROM dbo.Site WHERE SiteKey = N'load-a');
        DECLARE @b int = (SELECT Id FROM dbo.Site WHERE SiteKey = N'load-b');

        WITH n AS (SELECT TOP (60) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects)
        INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive)
        SELECT s.SiteId, CONCAT(N'load-cat-', FORMAT(n.i, '00')), CONCAT(N'Category ', n.i), n.i, 1
        FROM n
        CROSS JOIN (VALUES (@a), (@b)) s(SiteId)
        WHERE n.i <= 40;

        -- Store A maps feed categories 1-40, store B 21-60: twenty shared, as two real stores
        -- buying from one distributor would.
        WITH n AS (SELECT TOP (60) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects)
        INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId)
        SELECT @a, CONCAT(N'Feed ', FORMAT(n.i, '00')), c.Id
        FROM n JOIN dbo.SiteCategory c ON c.SiteId = @a AND c.SortOrder = n.i
        WHERE n.i <= 40
        UNION ALL
        SELECT @b, CONCAT(N'Feed ', FORMAT(n.i, '00')), c.Id
        FROM n JOIN dbo.SiteCategory c ON c.SiteId = @b AND c.SortOrder = n.i - 20
        WHERE n.i > 20;

        WITH n AS (
            SELECT TOP (@Products) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
            FROM sys.all_objects x CROSS JOIN sys.all_objects y)
        INSERT INTO dbo.Product (ProductName, Description, RetailPrice, QuantityInStock, Sku, Category,
                                 Cost, Source, Distributor, DistributorSku, LastSynced, Delisted,
                                 Manufacturer, ManufacturerPartNumber, Ean)
        SELECT CONCAT(N'Brand', n.i % 50, N' ',
                      CHOOSE(n.i % 6 + 1, N'ultrabook', N'desktop', N'monitor', N'dock', N'keyboard', N'server'),
                      N' model ', n.i),
               CONCAT(N'Load-check product ', n.i, N', a description long enough to be searched.'),
               5 + (n.i * 37) % 2000,
               n.i % 7,
               CONCAT(N'LD-', FORMAT(n.i, '000000')),
               CONCAT(N'Feed ', FORMAT(n.i % 60 + 1, '00')),
               (5 + (n.i * 37) % 2000) * 0.7,
               N'Distributor', N'Load', CONCAT(N'D', n.i),
               DATEADD(HOUR, -(n.i % 72), SYSUTCDATETIME()),
               CASE WHEN n.i % 97 = 0 THEN 1 ELSE 0 END,
               CONCAT(N'Brand', n.i % 50),
               CONCAT(N'MPN-', n.i),
               CONCAT(N'5', FORMAT(n.i, '000000000000'))
        FROM n;

        INSERT INTO dbo.CustomerGroup (Name, Slug, Discount, Terms, SiteId)
        VALUES (N'Load resellers', N'load-resellers', 12, N'Net 30', @a);

        -- Overrides on store A: hide a thousand it maps, show five hundred it does not, feature
        -- five hundred.
        INSERT INTO dbo.SiteProduct (SiteId, ProductId, Visibility, SiteCategoryId, Featured, Badge)
        SELECT TOP (1000) @a, p.Id, N'Hide', NULL, 0, NULL
        FROM dbo.Product p WHERE p.Sku LIKE N'LD-%' AND p.Category <= N'Feed 40' ORDER BY p.Id;

        INSERT INTO dbo.SiteProduct (SiteId, ProductId, Visibility, SiteCategoryId, Featured, Badge)
        SELECT TOP (500) @a, p.Id, N'Show', (SELECT TOP (1) Id FROM dbo.SiteCategory WHERE SiteId = @a ORDER BY SortOrder), 0, NULL
        FROM dbo.Product p WHERE p.Sku LIKE N'LD-%' AND p.Category > N'Feed 40' ORDER BY p.Id;

        INSERT INTO dbo.SiteProduct (SiteId, ProductId, Visibility, SiteCategoryId, Featured, Badge)
        SELECT TOP (500) @a, p.Id, NULL, NULL, 1, N'Best seller'
        FROM dbo.Product p
        WHERE p.Sku LIKE N'LD-%' AND p.Category <= N'Feed 40'
          AND NOT EXISTS (SELECT 1 FROM dbo.SiteProduct sp WHERE sp.SiteId = @a AND sp.ProductId = p.Id)
        ORDER BY p.Id DESC;

        COMMIT;
        """;
}
