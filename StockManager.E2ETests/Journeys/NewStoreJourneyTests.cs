using System.Net;
using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 12: an admin of every store creates a store in the portal, is refused opening it, sets
/// it up from the portal's own screens, opens it, and its storefront serves what they typed.
/// </summary>
/// <remarks>
/// T9's exit: a store stood up with no SQL and no shared-code change. Until then every value this
/// journey types was set by updating dbo.Site, dbo.SiteCategory and dbo.SiteContent by hand, and a
/// store was live the moment its row existed. The hops are the stores screen, the API resolving a
/// store that is not open for an admin of every store, the settings, categories and content
/// screens, the activation checklist in spSite_SetActive, and the storefront resolving the new
/// domain from dbo.Site.
///
/// The storefront is asked over HTTP with the new domain as the Host header, because the suite
/// has only two names for loopback and other journeys own them. SiteResolutionMiddleware decides
/// the store from that header and nothing else, so this is the request a customer's browser makes.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class NewStoreJourneyTests
{
    private readonly AspireAppFixture _fixture;

    public NewStoreJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_admin_creates_a_store_sets_it_up_in_the_portal_and_opens_it()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var key = $"e2e-new-{unique}";
        var name = $"Zzz New store {unique}";
        var domain = $"{unique}.new.e2e.test";

        await using var context = await _fixture.NewContextAsync();
        var admin = await context.NewPageAsync();
        await AdminPortal.SignInAsync(admin, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);

        // --- Create it, closed ---------------------------------------------------------------
        await Goto(admin, "/admin/stores");
        await admin.Locator("button.add-store").ClickAsync(new() { Timeout = 20_000 });
        await Type(admin, "#new-store-name", name);
        await Type(admin, "#new-store-key", key);
        await Type(admin, "#new-store-domain", domain);
        await Type(admin, "#new-store-country", "GB");
        await Type(admin, "#new-store-currency", "GBP");
        await Type(admin, "#new-store-locale", "en-GB");
        await admin.Locator("button.create-store").ClickAsync();
        await Expect(admin.Locator(".toast")).ToContainTextAsync("created, closed", AdminPortal.AfterReload);

        var row = admin.Locator($".store-row[data-key='{key}']");

        // --- Refused, with the list -----------------------------------------------------------
        await row.Locator("button.open-store").ClickAsync();
        await Expect(admin.Locator(".store-refusal")).ToContainTextAsync("still needs", AdminPortal.AfterReload);

        // --- Set it up, acting for it ---------------------------------------------------------
        await row.Locator("button.act-for").ClickAsync();
        await Expect(admin.Locator(".toast")).ToContainTextAsync($"Now showing {name}", AdminPortal.AfterReload);

        await Goto(admin, "/admin/store");
        await Type(admin, "#store-mail-from", $"orders@{domain}");
        await Type(admin, "#store-operator", $"ops@{domain}");
        await Type(admin, "#store-tax-rate", "20");
        await Type(admin, "#store-vat-number", "GB123456789");
        await Type(admin, "#store-legal-name", $"New Store {unique} Limited");
        await admin.Locator("button.store-settings-save").ClickAsync();
        await Expect(admin.Locator(".toast")).ToContainTextAsync("Store settings saved", AdminPortal.AfterReload);

        await Goto(admin, "/admin/categories");
        await admin.Locator("button.add-category").ClickAsync(new() { Timeout = 20_000 });
        await Type(admin, "#category-name", "Laptops");
        await admin.Locator("button.category-save").ClickAsync();
        await Expect(admin.Locator(".toast")).ToContainTextAsync("Laptops added", AdminPortal.AfterReload);

        await Goto(admin, "/admin/content");
        await WritePage(admin, "home", $"Welcome to {name}", "<p>Trade IT for the UK.</p>");
        await WritePage(admin, "terms", "Terms of sale", "<p>Net 30 on approved accounts.</p>");
        await WritePage(admin, "privacy", "Privacy", "<p>What we keep and why.</p>");
        await WritePage(admin, "cookies", "Cookies", "<p>Only the ones the shop needs.</p>");

        // --- Open it ---------------------------------------------------------------------------
        await Goto(admin, "/admin/stores");
        await row.Locator("button.open-store").ClickAsync(new() { Timeout = 20_000 });
        await Expect(admin.Locator(".toast")).ToContainTextAsync($"{name} is open", AdminPortal.AfterReload);

        // --- Its storefront serves what the admin typed ----------------------------------------
        var home = await Storefront(domain, "/");
        Assert.Equal(HttpStatusCode.OK, home.Status);
        Assert.Contains($"Welcome to {name}", home.Body);
        Assert.Contains("/catalog?cat=laptops", home.Body);
        Assert.Contains($"New Store {unique} Limited", home.Body);

        var terms = await Storefront(domain, "/terms");
        Assert.Contains("Net 30 on approved accounts.", terms.Body);
    }

    private Task Goto(IPage page, string path) =>
        page.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, path).ToString());

    // Tab after each fill: Blazor's inputs bind on change, which leaving the box produces and
    // fill() alone does not. See StaffAccountJourneyTests.
    private static async Task Type(IPage page, string selector, string value)
    {
        var box = page.Locator(selector);
        await box.FillAsync(value, new() { Timeout = 20_000 });
        await box.PressAsync("Tab");
    }

    private static async Task WritePage(IPage admin, string key, string title, string body)
    {
        await admin.Locator($".content-page[data-key='{key}']").ClickAsync(new() { Timeout = 20_000 });
        await Type(admin, "#content-title", title);
        await Type(admin, "#content-body", body);
        await admin.Locator("button.content-save-button").ClickAsync();
        await Expect(admin.Locator(".content-status")).ToContainTextAsync("Saved", AdminPortal.AfterReload);
    }

    private async Task<(HttpStatusCode Status, string Body)> Storefront(string domain, string path)
    {
        using var handler = new HttpClientHandler
        {
            // The development certificate is not trusted in this process. See AspireAppFixture.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_fixture.SmStoreBaseUrl, path));
        request.Headers.Host = domain;

        using var response = await http.SendAsync(request);

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
