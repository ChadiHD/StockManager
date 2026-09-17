using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Which products a store's staleness threshold hides, and — mostly — which it must not.
/// </summary>
/// <remarks>
/// Against a real database for the same reason <see cref="CatalogPriceParityTests"/> is: the
/// thing under test is a T-SQL predicate with three-valued logic in it, and every way of
/// getting it wrong produces a wrong row set rather than an error. `Source` is nullable, so
/// `Source &lt;&gt; 'Distributor'` is UNKNOWN for own stock and a reasonable-looking predicate
/// hides the store's entire own-brand catalog the first time somebody sets a threshold. No C#
/// reimplementation can tell you that.
///
/// Skips without SMDATABASE_TEST_CONNECTION; see <see cref="TestDatabase"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class CatalogStalenessTests
{
    /// <summary>One product in a throwaway store, with whatever freshness the test needs.</summary>
    private sealed record Fixture(string Sku, string? Source, int? SyncedHoursAgo);

    [SkippableFact]
    public void AFreshDistributorProductIsVisibleAndAStaleOneIsNot()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var visible = Visible(
            staleAfterHours: 24,
            hideStale: true,
            new Fixture("FRESH", "Distributor", SyncedHoursAgo: 2),
            new Fixture("STALE", "Distributor", SyncedHoursAgo: 72));

        visible.Should().Contain("FRESH");
        visible.Should().NotContain("STALE",
            "the distributor has said nothing for 72 hours, so the quantity on that row is "
            + "whatever it was three days ago");
    }

    [SkippableFact]
    public void OwnStockIsNeverStale()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var visible = Visible(
            staleAfterHours: 1,
            hideStale: true,
            // Both shapes own stock actually takes: Source explicitly 'Own', and Source left
            // NULL by a row that predates the column. Neither has a LastSynced, and neither may
            // be hidden — a "LastSynced >= cutoff" predicate would hide both.
            new Fixture("OWN", "Own", SyncedHoursAgo: null),
            new Fixture("NOSOURCE", null, SyncedHoursAgo: null));

        visible.Should().Contain("OWN");
        visible.Should().Contain("NOSOURCE");
    }

    [SkippableFact]
    public void ADistributorProductThatWasNeverSyncedIsTreatedAsStale()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var visible = Visible(
            staleAfterHours: 24,
            hideStale: true,
            new Fixture("NEVER", "Distributor", SyncedHoursAgo: null));

        // The upsert always stamps LastSynced, so this cannot arise from a sync. If it arises
        // some other way, nothing knows when that stock was last confirmed.
        visible.Should().NotContain("NEVER");
    }

    [SkippableFact]
    public void NothingIsHiddenWhileTheStoreHasNotAskedForIt()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var ancient = new Fixture("ANCIENT", "Distributor", SyncedHoursAgo: 24 * 400);

        // Off, which is how every existing store is configured: this feature lands inert.
        Visible(staleAfterHours: 0, hideStale: false, ancient).Should().Contain("ANCIENT");

        // A threshold with hiding off is the "alert me but keep selling" setting, and it must
        // not hide. So is hiding on with no threshold — half-configured is not configured.
        Visible(staleAfterHours: 24, hideStale: false, ancient).Should().Contain("ANCIENT");
        Visible(staleAfterHours: 0, hideStale: true, ancient).Should().Contain("ANCIENT");
    }

    [SkippableFact]
    public void TheFacetCountsAgreeWithThePageTheyFilterTo()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = StalenessScenario.Create(connection, transaction,
                staleAfterHours: 24, hideStale: true,
                new Fixture("FRESH1", "Distributor", 2),
                new Fixture("FRESH2", "Distributor", 3),
                new Fixture("STALE1", "Distributor", 48));

            // The one property the whole extraction of fnCatalog_VisibleProducts exists to
            // protect. A facet count that includes a stale product while the page it filters to
            // excludes it is a bug with no symptom until somebody counts.
            store.SearchCount().Should().Be(2);
            store.FacetCategoryCount().Should().Be(2);
        }
    }

    [SkippableFact]
    public void AStaleProductIsAlsoGoneFromItsOwnDetailPage()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var store = StalenessScenario.Create(connection, transaction,
                staleAfterHours: 24, hideStale: true,
                new Fixture("FRESH", "Distributor", 2),
                new Fixture("STALE", "Distributor", 48));

            // A product hidden from the listing but reachable by URL is still one a customer can
            // be sent a link to and order.
            store.DetailExists("FRESH").Should().BeTrue();
            store.DetailExists("STALE").Should().BeFalse();
        }
    }

    private static List<string> Visible(
        int staleAfterHours, bool hideStale, params Fixture[] products)
    {
        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            return StalenessScenario
                .Create(connection, transaction, staleAfterHours, hideStale, products)
                .VisibleSkuSuffixes();
        }
    }

    /// <summary>
    /// A store with a staleness setting and products of chosen freshness, inside a transaction
    /// that is never committed.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CatalogScenario"/> rather than folded into it: that one exists
    /// to drive a price matrix and takes (retail, cost) pairs, and bending it to also carry a
    /// per-product sync time and two site switches would make both tests harder to read than
    /// two fixtures are.
    /// </remarks>
    private sealed class StalenessScenario
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly string _runId;

        private StalenessScenario(
            SqlConnection connection, SqlTransaction transaction, string runId, int siteId)
        {
            _connection = connection;
            _transaction = transaction;
            _runId = runId;
            SiteId = siteId;
        }

        public int SiteId { get; }

        public static StalenessScenario Create(
            SqlConnection connection,
            SqlTransaction transaction,
            int staleAfterHours,
            bool hideStale,
            params Fixture[] products)
        {
            // SiteKey and Domain are unique, and a rolled-back transaction still collides with
            // a concurrent one that has not rolled back yet.
            var runId = Guid.NewGuid().ToString("N")[..12];

            var siteId = Scalar(connection, transaction, """
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                      FeedStaleAfterHours, HideStaleProducts, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Staleness test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, @staleAfter, @hideStale, 1);
                """,
                ("@key", $"stale-{runId}"),
                ("@domain", $"{runId}.stale.invalid"),
                ("@staleAfter", staleAfterHours),
                ("@hideStale", hideStale));

            var feedValue = $"StaleCategory-{runId}";

            var categoryId = Scalar(connection, transaction, """
                INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@siteId, @slug, N'Staleness test category', 1, 1);
                """,
                ("@siteId", siteId),
                ("@slug", $"stale-{runId}"));

            Execute(connection, transaction, """
                INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId)
                VALUES (@siteId, @feedValue, @categoryId);
                """,
                ("@siteId", siteId),
                ("@feedValue", feedValue),
                ("@categoryId", categoryId));

            foreach (var product in products)
            {
                Execute(connection, transaction, """
                    INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku,
                                             Category, QuantityInStock, Published, Delisted,
                                             [Source], LastSynced)
                    VALUES (@name, N'Staleness fixture.', 100, @sku,
                            @feedValue, 5, 1, 0, @source, @lastSynced);
                    """,
                    ("@name", $"Staleness product {product.Sku}"),
                    ("@sku", $"STL-{runId}-{product.Sku}"),
                    ("@feedValue", feedValue),
                    ("@source", (object?)product.Source ?? DBNull.Value),
                    ("@lastSynced", product.SyncedHoursAgo is { } hours
                        ? DateTime.UtcNow.AddHours(-hours)
                        : DBNull.Value));
            }

            return new StalenessScenario(connection, transaction, runId, siteId);
        }

        /// <summary>The trailing name of every SKU the catalog would show.</summary>
        public List<string> VisibleSkuSuffixes()
        {
            using var command = Command("""
                SELECT [Sku]
                FROM dbo.fnCatalog_VisibleProducts(
                    @siteId, NULL, 0, NULL, NULL, 0, NULL, NULL, NULL, 0, 0,
                    dbo.fnSite_StaleBeforeUtc(@siteId));
                """, ("@siteId", SiteId));

            using var reader = command.ExecuteReader();

            var skus = new List<string>();

            while (reader.Read())
            {
                skus.Add(reader.GetString(0).Replace($"STL-{_runId}-", string.Empty));
            }

            return skus;
        }

        /// <summary>How many rows the real search procedure returns.</summary>
        public int SearchCount()
        {
            using var command = Command(
                "EXEC dbo.spCatalog_Search @SiteId = @siteId;", ("@siteId", SiteId));

            using var reader = command.ExecuteReader();

            var rows = 0;
            while (reader.Read()) rows++;

            return rows;
        }

        /// <summary>The count the facet procedure reports for this store's one category.</summary>
        public int FacetCategoryCount()
        {
            using var command = Command(
                "EXEC dbo.spCatalog_GetFacets @SiteId = @siteId;", ("@siteId", SiteId));

            using var reader = command.ExecuteReader();

            // The first result set is categories: Slug, Name, Count.
            return reader.Read() ? reader.GetInt32(2) : 0;
        }

        public bool DetailExists(string skuSuffix)
        {
            using var command = Command(
                "EXEC dbo.spCatalog_GetBySku @SiteId = @siteId, @Sku = @sku;",
                ("@siteId", SiteId),
                ("@sku", $"STL-{_runId}-{skuSuffix}"));

            using var reader = command.ExecuteReader();

            return reader.Read();
        }

        private SqlCommand Command(string sql, params (string Name, object Value)[] parameters)
        {
            var command = new SqlCommand(sql, _connection, _transaction);

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return command;
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
