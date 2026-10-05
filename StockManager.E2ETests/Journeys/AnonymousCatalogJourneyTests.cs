using FluentAssertions;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 2: an anonymous visitor pages the catalog, and a malformed page number never 500s.
/// </summary>
/// <remarks>
/// CLAUDE.md records both ?page=abc and an overflowing ?page= as bugs that have already
/// happened -- a query-string parameter Blazor binds as int? throws during binding, before any
/// handler-level validation runs, and an unclamped page number overflows the procedure's
/// int arithmetic. Catalog.razor now binds Page as a plain string and parses it defensively
/// (see SMStore/Components/Pages/Catalog.razor), which is the fix this journey is guarding.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class AnonymousCatalogJourneyTests
{
    private readonly AspireAppFixture _fixture;

    public AnonymousCatalogJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Anonymous_visitor_pages_the_catalog_and_a_malformed_page_number_never_500s()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var site = await SqlTestData.GetOrCreateSiteForHostAsync(
            _fixture.SmDatabaseConnectionString, _fixture.SmStoreBaseUrl.Host);

        // CatalogPresenter pages at 24 (SMStore/Catalog/CatalogPresenter.cs). 30 products under
        // one fresh, GUID-named category give a deterministic page 1 (24) and page 2 (6)
        // regardless of anything else this database holds, real feed-synced catalog included.
        const int total = 30;
        const int pageSize = 24;

        var category = await SqlTestData.CreateCategoryMappingAsync(_fixture.SmDatabaseConnectionString, site.Id);
        await SqlTestData.InsertProductsAsync(
            _fixture.SmDatabaseConnectionString, $"E2Epg{Guid.NewGuid():N}"[..10], category.FeedValue, total);

        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        /*
        Nothing on the shop window may call another host. The fonts used to come from Google,
        which sent every visitor's address there, and that is the reason a consent banner would
        be needed at all (T7 plan, 1n and item 8). Images are the one exception, and a known
        one: enrichment links product images from the content provider's servers, which A4
        decides before launch.
        */
        var thirdParty = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.ResourceType != "image"
                && !string.Equals(new Uri(request.Url).Host, _fixture.SmStoreBaseUrl.Host, StringComparison.OrdinalIgnoreCase))
            {
                thirdParty.Add($"{request.ResourceType} {request.Url}");
            }
        };

        await page.GotoAsync(_fixture.SmStoreBaseUrl.ToString());

        var catalogUrl = new Uri(_fixture.SmStoreBaseUrl, $"/catalog?cat={category.Slug}").ToString();

        var firstPageResponse = await page.GotoAsync(catalogUrl);
        firstPageResponse!.Status.Should().Be(200);
        await Expect(page.Locator(".product-card")).ToHaveCountAsync(pageSize);

        var secondPageResponse = await page.GotoAsync($"{catalogUrl}&page=2");
        secondPageResponse!.Status.Should().Be(200);
        await Expect(page.Locator(".product-card")).ToHaveCountAsync(total - pageSize);

        // The two historical bugs. The contract here is exactly "does not 500" -- not which
        // page either lands on, which CatalogPresenter.Search is free to decide by
        // re-querying (see its remarks on an empty page carrying no total).
        var nonNumericPageResponse = await page.GotoAsync($"{catalogUrl}&page=abc");
        nonNumericPageResponse!.Status.Should().BeLessThan(500);

        var overflowPageResponse = await page.GotoAsync($"{catalogUrl}&page=99999999999");
        overflowPageResponse!.Status.Should().BeLessThan(500);

        thirdParty.Should().BeEmpty("a storefront page contacted another host, which tells that host who is browsing");
    }
}
