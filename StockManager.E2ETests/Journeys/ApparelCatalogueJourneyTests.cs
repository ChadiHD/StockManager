using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 14: an admin builds a shirt in two sizes with a photograph, and a shopper finds it
/// once, filters it by size, and buys a size (T11).
/// </summary>
/// <remarks>
/// <para>The hops no unit test spans:</para>
/// <list type="bullet">
/// <item>the portal's panels, posting variants and a multipart upload through the API;</item>
/// <item>spProduct_AddVariant and the blob store;</item>
/// <item>the photograph served back from <c>/img/</c> on both hosts;</item>
/// <item>the catalogue's roll-up, which lists the style once;</item>
/// <item>the option filter;</item>
/// <item>the product page's choice, posting a variant's SKU to a basket that refuses the style
/// itself.</item>
/// </list>
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class ApparelCatalogueJourneyTests
{
    // A real one-pixel PNG: ProductImageTypes reads the signature, and a browser draws it.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly AspireAppFixture _fixture;

    public ApparelCatalogueJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Admin_builds_a_shirt_in_sizes_and_a_shopper_buys_one()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var db = _fixture.SmDatabaseConnectionString;
        var store = _fixture.SmStoreBaseUrl;
        var site = await SqlTestData.GetOrCreateSiteForHostAsync(db, store.Host);
        var category = await SqlTestData.CreateCategoryMappingAsync(db, site.Id);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var style = $"E2Eshirt-{unique}";
        // A style holds no stock of its own; its sizes do.
        await SqlTestData.InsertProductAsync(db, style, $"Linen shirt {unique}", category.FeedValue, 40m, 0);

        // --- The admin builds it -----------------------------------------------------------
        await using var adminContext = await _fixture.NewContextAsync();
        var admin = await adminContext.NewPageAsync();

        await AdminPortal.SignInAsync(admin, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);
        await AdminPortal.EnsureActingForSiteAsync(admin, site.SiteKey);
        await admin.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, $"/admin/products/{style}").ToString());

        // 20 seconds: a cold WebAssembly boot, as in QuoteRequestJourneyTests.
        await Expect(admin.Locator(".product-variants")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await AddVariantAsync(admin, $"{style}-M", "3", "M");
        await Expect(admin.Locator(".variant-table")).ToContainTextAsync("M / Navy", AdminPortal.AfterReload);
        await AddVariantAsync(admin, $"{style}-L", "0", "L");
        await Expect(admin.Locator(".variant-table")).ToContainTextAsync("L / Navy", AdminPortal.AfterReload);

        await admin.Locator("#image-upload").SetInputFilesAsync(new FilePayload
        {
            // The name claims JPEG; the bytes are PNG, and the bytes decide.
            Name = "shirt.jpg", MimeType = "image/jpeg", Buffer = OnePixelPng
        });
        await Expect(admin.Locator(".image-strip img")).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        // The portal shows it from its own origin.
        var portalSrc = await admin.Locator(".image-strip img").GetAttributeAsync("src");
        var fromPortal = await admin.APIRequest.GetAsync(new Uri(_fixture.SmPortalBaseUrl, portalSrc!).ToString());
        Assert.Equal(200, fromPortal.Status);
        Assert.Equal("image/png", fromPortal.Headers["content-type"]);

        // --- The shopper finds it once, with its photograph --------------------------------
        await using var shopperContext = await _fixture.NewContextAsync();
        var shop = await shopperContext.NewPageAsync();

        await shop.GotoAsync(new Uri(store, $"/catalog?cat={category.Slug}").ToString());
        await Expect(shop.Locator(".product-card")).ToHaveCountAsync(1);
        await Expect(shop.Locator(".product-card__sku")).ToHaveTextAsync($"SKU {style}");
        await Expect(shop.Locator("a.product-card__add")).ToHaveTextAsync("Choose options");

        var storeSrc = await shop.Locator(".product-card img").GetAttributeAsync("src");
        Assert.StartsWith("/img/", storeSrc);
        var fromStore = await shop.APIRequest.GetAsync(new Uri(store, storeSrc!).ToString());
        Assert.Equal(200, fromStore.Status);
        Assert.Contains("immutable", fromStore.Headers["cache-control"]);

        // --- Filtered by size --------------------------------------------------------------
        await shop.GotoAsync(new Uri(store, $"/catalog?cat={category.Slug}&opt=Size:M").ToString());
        await Expect(shop.Locator(".product-card")).ToHaveCountAsync(1);

        await shop.GotoAsync(new Uri(store, $"/catalog?cat={category.Slug}&opt=Size:XL").ToString());
        await Expect(shop.Locator(".product-card")).ToHaveCountAsync(0);

        // In stock in L means nothing: L has none.
        await shop.GotoAsync(new Uri(store, $"/catalog?cat={category.Slug}&opt=Size:L&stock=1").ToString());
        await Expect(shop.Locator(".product-card")).ToHaveCountAsync(0);

        // --- And buys a size -----------------------------------------------------------------
        await shop.GotoAsync(new Uri(store, $"/product/{style}").ToString());
        await Expect(shop.Locator("#add-variant option")).ToHaveCountAsync(2);
        await Expect(shop.Locator("#add-variant")).ToContainTextAsync("L / Navy (out of stock)");

        await shop.Locator("#add-variant").SelectOptionAsync($"{style}-M");
        await shop.Locator("form.product__add button[type=submit]").ClickAsync();

        await shop.GotoAsync(new Uri(store, "/quote").ToString());
        await Expect(shop.Locator("table.basket__table tbody tr")).ToHaveCountAsync(1);
        // The variant's own name, so the quote and the order say which size was bought.
        await Expect(shop.Locator("table.basket__table")).ToContainTextAsync($"Linen shirt {unique} — M / Navy");
    }

    /// <summary>
    /// Types into the add-variant form. Each <c>@bind</c> commits on change, which Playwright's
    /// fill alone does not drive; a Tab after it is what a person typing produces.
    /// </summary>
    private static async Task AddVariantAsync(IPage admin, string sku, string stock, string size)
    {
        foreach (var (selector, value) in new[]
                 {
                     ("#variant-sku", sku), ("#variant-stock", stock),
                     ("#variant-name-1", "Size"), ("#variant-value-1", size),
                     ("#variant-name-2", "Colour"), ("#variant-value-2", "Navy")
                 })
        {
            await admin.Locator(selector).FillAsync(value);
            await admin.Locator(selector).PressAsync("Tab");
        }

        await admin.Locator(".variant-save").ClickAsync();
    }
}
