using System.Text.RegularExpressions;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 9: an admin decides what one store sells without changing the other, and a store
/// that hides its prices hides them from everybody who has not signed in.
/// </summary>
/// <remarks>
/// T8's exit criterion, across the two stores the suite already runs (<c>localhost</c> and
/// <c>127.0.0.1</c> resolve to different sites). Both sell the same product to begin with,
/// because they map the same feed category. An admin acting for store A hides it, and shows
/// another product whose feed category store A does not map, under one of store A's
/// categories. Store A's shop changes; store B's does not.
///
/// The hops are the ones the plan was written around: the portal's per-store panel, the API
/// scoped by the acting store, <c>dbo.SiteProduct</c>, and <c>fnSite_ProductPlacement</c> read
/// by the storefront's catalog. No unit test spans the portal and the shop.
///
/// Then the price half. Store A is set to show prices only after sign-in for the journey, and
/// put back afterwards. An anonymous visitor sees no price and is offered no price sort, even by
/// asking for one in the URL. A signed-in customer whose account has no pricing group — the case
/// <c>ShowPrices</c> used to get wrong — sees list price.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class StoreCatalogJourneyTests
{
    private readonly AspireAppFixture _fixture;

    public StoreCatalogJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Admin_curates_one_store_and_its_prices_stay_behind_sign_in()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var db = _fixture.SmDatabaseConnectionString;
        var hostA = _fixture.SmStoreBaseUrl.Host;
        var hostB = hostA == "127.0.0.1" ? "localhost" : "127.0.0.1";
        var storeA = _fixture.SmStoreBaseUrl;
        var storeB = new UriBuilder(_fixture.SmStoreBaseUrl) { Host = hostB }.Uri;

        var siteA = await SqlTestData.GetOrCreateSiteForHostAsync(db, hostA);
        var siteB = await SqlTestData.GetOrCreateSiteForHostAsync(db, hostB);
        var previousDisplay = await SqlTestData.SetPriceDisplayAsync(db, siteA.Id, "Public");

        try
        {
            // The same feed category, mapped by both stores, so both sell what is filed in it.
            var categoryA = await SqlTestData.CreateCategoryMappingAsync(db, siteA.Id);
            await SqlTestData.CreateCategoryMappingAsync(db, siteB.Id, categoryA.FeedValue);

            var unique = Guid.NewGuid().ToString("N")[..8];
            var curatedSku = $"E2Ecur-{unique}";
            var shownSku = $"E2Eshw-{unique}";

            await SqlTestData.InsertProductAsync(db, curatedSku, $"Curated {unique}", categoryA.FeedValue, 100m, 5);
            // Filed under a feed category neither store maps: on no store until somebody says so.
            await SqlTestData.InsertProductAsync(db, shownSku, $"Shown {unique}", $"E2E-unmapped-{unique}", 100m, 5);

            await using var shopper = await _fixture.NewContextAsync();
            var shop = await shopper.NewPageAsync();

            await shop.GotoAsync(new Uri(storeA, $"/product/{curatedSku}").ToString());
            await Expect(shop.Locator("h1")).ToContainTextAsync($"Curated {unique}");

            // --- The admin decides for store A -----------------------------------------------
            await using var adminContext = await _fixture.NewContextAsync();
            var admin = await adminContext.NewPageAsync();

            await AdminPortal.SignInAsync(admin, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);
            await AdminPortal.EnsureActingForSiteAsync(admin, siteA.SiteKey);

            await admin.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, $"/admin/products/{curatedSku}").ToString());

            // 20 seconds: a cold WebAssembly boot, as in QuoteRequestJourneyTests.
            await Expect(admin.Locator(".placement-status")).ToContainTextAsync("Listed", new() { Timeout = 20_000 });
            await admin.Locator("#placement-visibility").SelectOptionAsync("Hide");
            await admin.Locator("button.placement-save").ClickAsync();
            await Expect(admin.Locator(".placement-status")).ToContainTextAsync("Hidden", AdminPortal.AfterReload);

            await admin.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, $"/admin/products/{shownSku}").ToString());
            await Expect(admin.Locator(".placement-status")).ToContainTextAsync("not mapped", new() { Timeout = 20_000 });
            await admin.Locator("#placement-visibility").SelectOptionAsync("Show");
            await admin.Locator("#placement-category").SelectOptionAsync(new Microsoft.Playwright.SelectOptionValue { Label = categoryA.Name });
            await admin.Locator("button.placement-save").ClickAsync();
            await Expect(admin.Locator(".placement-status")).ToContainTextAsync("Shown by hand", AdminPortal.AfterReload);

            // --- Store A changed ---------------------------------------------------------------
            // Indistinguishable from a wrong SKU, on purpose: a guessed SKU cannot confirm that a
            // product exists but is withheld.
            await shop.GotoAsync(new Uri(storeA, $"/product/{curatedSku}").ToString());
            await Expect(shop).ToHaveTitleAsync(new Regex("Product not found"));

            await shop.GotoAsync(new Uri(storeA, $"/catalog?cat={categoryA.Slug}").ToString());
            await Expect(shop.Locator(".product-card__sku")).ToHaveTextAsync([$"SKU {shownSku}"]);

            // --- Store B did not ---------------------------------------------------------------
            await shop.GotoAsync(new Uri(storeB, $"/product/{curatedSku}").ToString());
            await Expect(shop.Locator("h1")).ToContainTextAsync($"Curated {unique}");

            await shop.GotoAsync(new Uri(storeB, $"/product/{shownSku}").ToString());
            await Expect(shop).ToHaveTitleAsync(new Regex("Product not found"));

            // --- Prices behind sign-in on store A ------------------------------------------
            await SqlTestData.SetPriceDisplayAsync(db, siteA.Id, "Authenticated");

            // The fixture caches a site for a second; wait it out rather than race it.
            await Task.Delay(TimeSpan.FromSeconds(1.5));

            await shop.GotoAsync(new Uri(storeA, $"/catalog?cat={categoryA.Slug}").ToString());
            await Expect(shop.Locator(".product-card__amount")).ToHaveCountAsync(0);
            await Expect(shop.Locator(".product-card__note")).ToContainTextAsync("Sign in to see pricing");
            // The order alone would rank the prices, so the sort is not offered...
            await Expect(shop.Locator(".catalog__sort")).Not.ToContainTextAsync("Price");

            // ...and asking for it in the URL gets the default instead.
            await shop.GotoAsync(new Uri(storeA, $"/catalog?cat={categoryA.Slug}&sort=price-asc").ToString());
            await Expect(shop.Locator(".catalog__sort .is-current")).ToHaveTextAsync("Featured");

            // An approved customer nobody has put in a pricing group is still a registered one.
            var customer = await IdentityTestSupport.CreateApprovedCustomerAsync(
                _fixture.ApiAuthConnectionString, db, siteA.SiteKey, siteA.Id);

            await using var customerContext = await _fixture.NewContextAsync();
            var signedIn = await customerContext.NewPageAsync();

            await StoreFront.SignInAsync(signedIn, storeA, customer.Email, customer.Password);
            await signedIn.GotoAsync(new Uri(storeA, $"/catalog?cat={categoryA.Slug}").ToString());
            await Expect(signedIn.Locator(".product-card__amount")).ToContainTextAsync("€100.00");
        }
        finally
        {
            await SqlTestData.SetPriceDisplayAsync(db, siteA.Id, previousDisplay);
        }
    }
}
