using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 1: an admin changes stock on a product in SMPortal, and the change is visible on
/// the customer-facing SMStore product page.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class StockVisibilityJourneyTests
{
    private readonly AspireAppFixture _fixture;

    public StockVisibilityJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Admin_restocks_a_product_and_the_storefront_reflects_it_immediately()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var site = await SqlTestData.GetOrCreateSiteForHostAsync(
            _fixture.SmDatabaseConnectionString, _fixture.SmStoreBaseUrl.Host);

        var category = await SqlTestData.CreateCategoryMappingAsync(_fixture.SmDatabaseConnectionString, site.Id);

        var sku = $"E2E-{Guid.NewGuid():N}"[..20];

        // Starts out of stock, so "In stock" after the edit below is evidence the edit
        // actually reached the storefront rather than a value the page would show regardless.
        await SqlTestData.InsertProductAsync(
            _fixture.SmDatabaseConnectionString, sku, "E2E stock visibility widget",
            category.FeedValue, retailPrice: 42.00m, quantityInStock: 0);

        await using var context = await _fixture.NewContextAsync();

        var storePage = await context.NewPageAsync();
        var productUrl = new Uri(_fixture.SmStoreBaseUrl, $"/product/{sku}").ToString();

        await storePage.GotoAsync(productUrl);
        await Expect(storePage.Locator(".product__buy span.tag")).ToHaveTextAsync("Lead time on request");

        // Product is not scoped by Site at all (dbo.Product carries no SiteId -- see
        // CategoryMapping.sql), so ProductController needs no X-Site-Key and there is nothing
        // to switch the admin session to before editing it.
        var adminPage = await context.NewPageAsync();
        await AdminPortal.SignInAsync(adminPage, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);

        await adminPage.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, $"/admin/products/{sku}").ToString());

        await adminPage.FieldControl("Available units").FillAsync("25");
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(adminPage.Locator(".toast")).ToHaveTextAsync("Product saved");

        await storePage.ReloadAsync();
        await Expect(storePage.Locator(".product__buy span.tag")).ToHaveTextAsync("In stock");
    }
}
