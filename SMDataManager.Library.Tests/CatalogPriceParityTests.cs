using Microsoft.Data.SqlClient;
using SMDataManager.Library.Pricing;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Pins the net-price expression in dbo.fnCatalog_VisibleProducts to
/// <see cref="PriceResolver"/>.
/// </summary>
/// <remarks>
/// The catalog sorts on a price computed in SQL and displays a price computed in C#. That is
/// one rule written twice, accepted deliberately — paging in memory does not survive the SKU
/// counts this platform is sized for, and precomputing per product and group adds an
/// invalidation surface larger than the rule itself. This test is the tripwire that makes the
/// duplication safe: change either copy without the other and it goes red.
///
/// It is not a unit test and cannot be. Evaluating the SQL half needs SQL Server;
/// reimplementing it in C# would be a third copy of the thing under test.
/// </remarks>
public class CatalogPriceParityTests
{
    private readonly IPriceResolver _resolver = new PriceResolver();

    /// <summary>
    /// List price and cost pairs chosen for the edges rather than for coverage: a cost above
    /// list so the floor has to be capped, a null and a zero cost so it is skipped, a zero
    /// list, a cost equal to list, and a list price carrying a half-cent — money holds four
    /// decimal places and the floor is capped against the *unrounded* list, which is the one
    /// place an obvious reimplementation diverges.
    /// </summary>
    private static readonly (decimal Retail, decimal? Cost)[] Products =
    [
        (100.00m, 80.00m),
        (100.00m, 95.00m),
        (100.00m, 140.00m),
        (100.00m, null),
        (100.00m, 0.00m),
        (0.00m, 10.00m),
        (9.99m, 9.00m),
        (10.005m, 9.00m),
        (199.99m, 149.99m),
        (1.00m, 0.99m),
        (33.33m, 11.11m),
        (2499.00m, 2498.99m)
    ];

    private static readonly int[] Discounts = [0, 1, 10, 33, 50, 99, 100];

    private static readonly decimal[] Margins = [0m, 3.33m, 7.50m, 12m, 20m, 50m];

    [SkippableFact]
    public void SqlNetPriceMatchesPriceResolver()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();

        using (connection)
        using (transaction)
        {
            var scenario = CatalogScenario.Create(connection, transaction, Products);

            var mismatches = new List<string>();

            foreach (var discount in Discounts)
            foreach (var margin in Margins)
            {
                foreach (var row in scenario.ReadNetPrices(discount, margin))
                {
                    var expected = _resolver
                        .Resolve(row.Retail, row.Cost, discount, margin)
                        .NetPrice;

                    if (expected != row.NetPrice)
                    {
                        mismatches.Add(
                            $"list={row.Retail} cost={(row.Cost?.ToString() ?? "null")} " +
                            $"discount={discount} margin={margin}: " +
                            $"sql={row.NetPrice} resolver={expected}");
                    }
                }
            }

            Assert.True(mismatches.Count == 0,
                $"{mismatches.Count} of {Products.Length * Discounts.Length * Margins.Length} " +
                "combinations disagree between dbo.fnCatalog_VisibleProducts and PriceResolver:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, mismatches.Take(20)));

            // Left uncommitted on purpose; see TestDatabase.OpenRollbackScope.
            transaction.Rollback();
        }
    }

    /// <summary>
    /// The reason the expression exists at all: with a floor in play, ordering by list price
    /// and ordering by net price give different sequences. If this ever passes trivially the
    /// fixture has stopped exercising the case the sort was changed for.
    /// </summary>
    [SkippableFact]
    public void FloorChangesTheOrderOfAPriceSort()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();

        using (connection)
        using (transaction)
        {
            var scenario = CatalogScenario.Create(connection, transaction, Products);

            // A deep discount with a floor under it is what separates the two orderings: the
            // discount pushes everything down, and the floor holds the thin-margin rows up.
            var rows = scenario.ReadNetPrices(discountPct: 50, minMarginPct: 20m).ToList();

            var byList = rows.OrderBy(r => r.Retail).ThenBy(r => r.Sku).Select(r => r.Sku).ToList();
            var byNet = rows.OrderBy(r => r.NetPrice).ThenBy(r => r.Sku).Select(r => r.Sku).ToList();

            Assert.NotEqual(byList, byNet);

            transaction.Rollback();
        }
    }
}
