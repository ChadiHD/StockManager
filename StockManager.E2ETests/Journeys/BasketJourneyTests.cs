using FluentAssertions;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 6: an anonymous visitor fills a basket, changes it, and empties it.
/// </summary>
/// <remarks>
/// Every basket change is a form post plus a redirect, because the storefront renders with
/// static SSR and has no circuit for an event handler. That means three things no unit test
/// touches, and all three have to hold at once for a single click to work: the antiforgery
/// token has to validate, the <c>Set-Cookie</c> carrying the basket token has to survive the
/// redirect, and the next request has to find the basket that cookie names.
///
/// <c>BasketServiceTests</c> asserts the cookie's attributes against a fake HttpContext and
/// <c>BasketPageTests</c> asserts the form's fields against a rendered DOM. Neither can tell
/// you that a real browser posts that form to that endpoint and comes back holding that basket
/// — which is the same gap the approval journey found in T3, where four unit-test projects
/// agreed on behaviour the database refused.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class BasketJourneyTests
{
    private readonly AspireAppFixture _fixture;

    public BasketJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Anonymous_visitor_adds_changes_and_removes_a_basket_line()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var site = await SqlTestData.GetOrCreateSiteForHostAsync(
            _fixture.SmDatabaseConnectionString, _fixture.SmStoreBaseUrl.Host);

        // Its own GUID-named category, so the products this journey adds are the only ones the
        // listing can show and the basket's contents are deterministic whatever else this
        // database holds.
        var category = await SqlTestData.CreateCategoryMappingAsync(
            _fixture.SmDatabaseConnectionString, site.Id);

        var skuPrefix = $"E2Ebk{Guid.NewGuid():N}"[..10];

        await SqlTestData.InsertProductsAsync(
            _fixture.SmDatabaseConnectionString, skuPrefix, category.FeedValue, count: 2);

        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        var catalogUrl = new Uri(_fixture.SmStoreBaseUrl, $"/catalog?cat={category.Slug}").ToString();

        await page.GotoAsync(catalogUrl);
        await Expect(page.Locator(".product-card")).ToHaveCountAsync(2);

        // A visitor with no cookie has no basket at all, so the header shows no count.
        await Expect(page.Locator(".site-header__count")).ToHaveCountAsync(0);

        // Add from the listing. The card's button is a form submit, not an onclick.
        await page.Locator(".product-card__add").First.ClickAsync();

        // Back on the listing rather than on the basket: adding from the catalog must not lose
        // the visitor's place in it.
        await page.WaitForURLAsync(url => url.Contains("/catalog"));
        await Expect(page.Locator(".site-header__count")).ToHaveTextAsync("1");

        // The cookie is the whole mechanism, so assert it exists and that a script could not
        // read it. Playwright reports HttpOnly from the browser's own cookie jar.
        var cookies = await context.CookiesAsync();
        var basketCookie = cookies.SingleOrDefault(cookie => cookie.Name == ".SMStore.Basket");

        basketCookie.Should().NotBeNull("the basket is found by the token in this cookie");
        basketCookie!.HttpOnly.Should().BeTrue();
        basketCookie.Value.Should().HaveLength(43, "256 bits of base64url, per BasketToken");

        // A second product, so the basket has two lines and the count is of products.
        await page.Locator(".product-card__add").Last.ClickAsync();
        await Expect(page.Locator(".site-header__count")).ToHaveTextAsync("2");

        await page.Locator(".site-header__basket").ClickAsync();
        await Expect(page.Locator("table.basket__table tbody tr")).ToHaveCountAsync(2);

        // Raise a quantity. The count stays at 2 because it counts products, not units.
        var firstQuantity = page.Locator("form.basket__qty").First;
        await firstQuantity.Locator("input[name=quantity]").FillAsync("4");
        await firstQuantity.Locator("button[type=submit]").ClickAsync();

        await Expect(page.Locator("form.basket__qty").First.Locator("input[name=quantity]"))
            .ToHaveValueAsync("4");
        await Expect(page.Locator(".site-header__count")).ToHaveTextAsync("2");

        // Remove one, which is a quantity of zero on the same endpoint.
        await page.Locator("form.basket__remove button").First.ClickAsync();

        await Expect(page.Locator("table.basket__table tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator(".site-header__count")).ToHaveTextAsync("1");

        // Empty it, and the page offers the catalog rather than an empty table.
        await page.Locator("form.basket__remove button").First.ClickAsync();

        await Expect(page.Locator("table.basket__table")).ToHaveCountAsync(0);
        await Expect(page.Locator(".site-header__count")).ToHaveCountAsync(0);
        await Expect(page.Locator(".stub a")).ToHaveAttributeAsync("href", "/catalog");
    }
}
