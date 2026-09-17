using Microsoft.Data.SqlClient;

namespace SMDataManager.Library.Tests;

/// <summary>
/// A throwaway store with a throwaway catalog, built inside a transaction so it never exists
/// as far as anything else is concerned.
/// </summary>
/// <remarks>
/// A product reaches a storefront only through a CategoryMapping row for that site, so
/// exercising the catalog function needs four rows of scaffolding before the first product:
/// a Site, a SiteCategory, the mapping, and then the products themselves. That is the design
/// working as intended rather than an awkward fixture — dbo.Product is shared with the
/// desktop POS and carries no site of its own.
/// </remarks>
internal sealed class CatalogScenario
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;

    private CatalogScenario(SqlConnection connection, SqlTransaction transaction, int siteId)
    {
        _connection = connection;
        _transaction = transaction;
        SiteId = siteId;
    }

    public int SiteId { get; }

    public sealed record Row(string Sku, decimal Retail, decimal? Cost, decimal NetPrice);

    public static CatalogScenario Create(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<(decimal Retail, decimal? Cost)> products)
    {
        // Unique per run: SiteKey and Domain are unique constraints, and a transaction that is
        // rolled back still collides with a concurrent one that has not been.
        var runId = Guid.NewGuid().ToString("N")[..12];

        var siteId = Scalar(connection, transaction, $"""
            INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                  OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@key, N'Parity test store', @domain, N'IE', N'EUR', N'en-IE',
                    N'Rfq', N'eu-b2b', N'Public', 0, 1);
            """,
            ("@key", $"test-{runId}"),
            ("@domain", $"{runId}.test.invalid"));

        var feedValue = $"TestCategory-{runId}";

        var categoryId = Scalar(connection, transaction, """
            INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@siteId, @slug, N'Parity test category', 1, 1);
            """,
            ("@siteId", siteId),
            ("@slug", $"parity-{runId}"));

        Execute(connection, transaction, """
            INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId)
            VALUES (@siteId, @feedValue, @categoryId);
            """,
            ("@siteId", siteId),
            ("@feedValue", feedValue),
            ("@categoryId", categoryId));

        for (int index = 0; index < products.Count; index++)
        {
            var (retail, cost) = products[index];

            Execute(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Cost, Sku,
                                         Category, QuantityInStock, Published, Delisted)
                VALUES (@name, N'Parity fixture.', @retail, @cost, @sku,
                        @feedValue, 5, 1, 0);
                """,
                ("@name", $"Parity product {index:D2}"),
                ("@retail", retail),
                ("@cost", (object?)cost ?? DBNull.Value),
                // Ordinal-prefixed so a failure message names the matrix row it came from.
                ("@sku", Sku(runId, index)),
                ("@feedValue", feedValue));
        }

        return new CatalogScenario(connection, transaction, siteId);
    }

    private static string Sku(string runId, int index) => $"PAR-{runId}-{index:D2}";

    /// <summary>
    /// Calls the function under test the way spCatalog_Search calls it: no term, no filters,
    /// and the discount and margin resolved by the caller rather than read from any row.
    /// </summary>
    public IEnumerable<Row> ReadNetPrices(int discountPct, decimal minMarginPct)
    {
        using var command = new SqlCommand("""
            SELECT [Sku], [RetailPrice], [Cost], [NetPrice]
            FROM dbo.fnCatalog_VisibleProducts(
                -- The trailing NULL is @StaleBeforeUtc: this test is about the price
                -- expression, and a cutoff would hide rows it is trying to compare.
                @siteId, NULL, 0, NULL, NULL, 0, NULL, NULL, NULL, @discount, @margin, NULL)
            ORDER BY [Sku];
            """, _connection, _transaction);

        command.Parameters.AddWithValue("@siteId", SiteId);
        command.Parameters.AddWithValue("@discount", discountPct);
        command.Parameters.AddWithValue("@margin", minMarginPct);

        using var reader = command.ExecuteReader();

        var rows = new List<Row>();

        while (reader.Read())
        {
            rows.Add(new Row(
                reader.GetString(0),
                reader.GetDecimal(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.GetDecimal(3)));
        }

        return rows;
    }

    private static int Scalar(
        SqlConnection connection, SqlTransaction transaction,
        string sql, params (string Name, object Value)[] parameters)
    {
        using var command = new SqlCommand(sql, connection, transaction);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (int)command.ExecuteScalar()!;
    }

    private static void Execute(
        SqlConnection connection, SqlTransaction transaction,
        string sql, params (string Name, object Value)[] parameters)
    {
        using var command = new SqlCommand(sql, connection, transaction);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
