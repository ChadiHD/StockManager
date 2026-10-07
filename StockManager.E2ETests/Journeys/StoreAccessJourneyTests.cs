using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 11: an admin of every store gives a colleague one store, the colleague can act for
/// that store and no other, and taking it away applies to the colleague's next request.
/// </summary>
/// <remarks>
/// T9. Until then every admin could act for every store, so a second tenant's staff would have
/// seen the first tenant's customers, quotes and prices. The hops are the users screen's store
/// picker, Register handing back the new login's id, the grant written to dbo.UserSite, the
/// portal's selector offering only what GET api/Site returns, and the per-request check in
/// AdminSiteResolutionMiddleware — which is not cached, so revocation is not "within a minute".
///
/// The refusals are asked of the API directly, with the colleague's own token: a portal that
/// merely hides a store proves nothing about the request somebody types by hand.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class StoreAccessJourneyTests
{
    private const string Password = "Store-Passw0rd!";

    private readonly AspireAppFixture _fixture;

    public StoreAccessJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_admin_given_one_store_acts_for_it_alone_until_it_is_taken_away()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var db = _fixture.SmDatabaseConnectionString;
        var hostA = _fixture.SmStoreBaseUrl.Host;
        var hostB = hostA == "127.0.0.1" ? "localhost" : "127.0.0.1";
        var siteA = await SqlTestData.GetOrCreateSiteForHostAsync(db, hostA);
        var siteB = await SqlTestData.GetOrCreateSiteForHostAsync(db, hostB);

        var email = $"e2e-store-admin-{Guid.NewGuid():N}@example.test";

        // --- The admin of every store adds a colleague with store B only ----------------------
        await using var ownerContext = await _fixture.NewContextAsync();
        var owner = await ownerContext.NewPageAsync();

        await AdminPortal.SignInAsync(owner, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);
        await owner.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, "/admin/users").ToString());
        await owner.GetByRole(AriaRole.Button, new() { Name = "Add user" }).ClickAsync(new() { Timeout = 20_000 });

        var form = owner.Locator(".modal");
        // Tab after each fill: Blazor's InputText binds on change. See StaffAccountJourneyTests.
        await form.Locator("input").First.FillAsync("Siobhan Byrne");
        await form.Locator("input").First.PressAsync("Tab");
        await form.Locator("input[type=email]").FillAsync(email);
        await form.Locator("input[type=email]").PressAsync("Tab");
        await form.Locator("select").SelectOptionAsync("Admin");
        await form.Locator("input[type=password]").FillAsync(Password);
        await form.Locator("input[type=password]").PressAsync("Tab");
        await form.Locator($"input.store-one[value='{siteB.SiteKey}']").CheckAsync();
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(owner.Locator(".toast")).ToContainTextAsync("User added", AdminPortal.AfterReload);

        // --- The colleague: one store offered, the other refused -----------------------------
        await using var colleagueContext = await _fixture.NewContextAsync();
        var colleague = await colleagueContext.NewPageAsync();

        await AdminPortal.SignInAsync(colleague, _fixture.SmPortalBaseUrl, email, Password);

        // A workspace that loaded, for one store: the dashboard rather than "could not be
        // reached", no selector to choose another store, no "you have no store" notice, and no
        // way to the staff screen. 20 seconds: a cold WebAssembly boot.
        await Expect(colleague.Locator(".main-content h1")).ToContainTextAsync("Trading overview", new() { Timeout = 20_000 });
        await Expect(colleague.Locator(".alert.error")).ToHaveCountAsync(0);
        await Expect(colleague.Locator("#admin-site")).ToHaveCountAsync(0);
        await Expect(colleague.Locator(".no-store-access")).ToHaveCountAsync(0);
        await Expect(colleague.Locator("a[href='admin/users']")).ToHaveCountAsync(0);

        using var api = Api();
        var token = await TokenAsync(api, email, Password);

        var offered = await Get<List<SiteOptionDto>>(api, token, "/api/Site");
        Assert.Equal(new[] { siteB.SiteKey }, offered.Select(site => site.SiteKey).ToArray());

        Assert.Equal(HttpStatusCode.OK, (await Send(api, token, "/api/Account", siteB.SiteKey)).StatusCode);

        var refused = await Send(api, token, "/api/Account", siteA.SiteKey);
        var unknown = await Send(api, token, "/api/Account", $"no-such-store-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        // "Not yours" reads exactly like "not here", so the answer does not say which stores exist.
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await refused.Content.ReadAsStringAsync());

        // Staff are not one store's.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(api, token, "/api/User/Admin/GetAllUsers", siteB.SiteKey)).StatusCode);

        // --- The admin of every store takes store B away -------------------------------------
        await owner.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, "/admin/users").ToString());
        var search = owner.Locator(".toolbar input[type=search]");
        await search.FillAsync(email, new() { Timeout = 20_000 });
        await search.PressAsync("Enter");

        await owner.Locator("tr", new() { HasText = email })
            .GetByRole(AriaRole.Button, new() { Name = "Manage" }).ClickAsync();
        await owner.Locator($".modal input.store-one[value='{siteB.SiteKey}']").UncheckAsync();
        await owner.Locator(".modal").GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await Expect(owner.Locator(".toast")).ToContainTextAsync("Access updated", AdminPortal.AfterReload);

        // The same token, the very next request: no cache to wait out.
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(api, token, "/api/Account", siteB.SiteKey)).StatusCode);
        Assert.Empty(await Get<List<SiteOptionDto>>(api, token, "/api/Site"));

        // And the admin of every store still has both.
        var ownerToken = await TokenAsync(api, _fixture.Admin.Email, _fixture.Admin.Password);
        var everyStore = (await Get<List<SiteOptionDto>>(api, ownerToken, "/api/Site")).Select(site => site.SiteKey).ToList();
        Assert.Contains(siteA.SiteKey, everyStore);
        Assert.Contains(siteB.SiteKey, everyStore);
    }

    private sealed record SiteOptionDto(int Id, string SiteKey, string Name);

    private sealed record TokenDto(string Access_Token);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private HttpClient Api() => new(new HttpClientHandler
    {
        // The development certificate is not trusted in this process; a loopback call to the
        // host this fixture started. See AspireAppFixture.
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    { BaseAddress = _fixture.StockApiBaseUrl };

    private static async Task<string> TokenAsync(HttpClient api, string email, string password)
    {
        var response = await api.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = email,
            ["password"] = password
        }));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TokenDto>(Web))!.Access_Token;
    }

    private static Task<HttpResponseMessage> Send(HttpClient api, string token, string path, string? siteKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (siteKey is not null)
        {
            request.Headers.Add("X-Site-Key", siteKey);
        }

        return api.SendAsync(request);
    }

    private static async Task<T> Get<T>(HttpClient api, string token, string path)
    {
        var response = await Send(api, token, path);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(Web))!;
    }
}
