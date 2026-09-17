using Xunit;

// Belt and braces alongside every test sharing the one collection below: xUnit v2 already runs
// the tests within one collection sequentially by default (parallelism happens *across*
// collections), and this assembly only defines the one, but saying so here means a test added
// later without a [Collection] attribute still cannot race the four that matter. Every journey
// works against the same SQL Server container and the same running sm-portal/sm-store/
// stock-api, and two of them racing -- one inserting a Site row that changes what SMPortal's
// store switcher defaults to while another is mid-journey, say -- is a false failure with
// nothing wrong in the product underneath it. Slow and serial beats fast and flaky here.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace StockManager.E2ETests.Infrastructure;

/// <summary>
/// One Aspire app host and one browser for every test in this assembly.
/// </summary>
[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<AspireAppFixture>
{
    public const string Name = "StockManager E2E";
}
