using FluentAssertions;
using Microsoft.Playwright;
using SMStore.Accounts;
using StockManager.E2ETests.Infrastructure;
using Xunit;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 4: a customer session at one store must not work at another -- the platform's
/// central security property (CLAUDE.md: "Sign a customer in to one store and no other").
/// </summary>
/// <remarks>
/// Reaches one running sm-store as two tenants by giving a second Site row the domain
/// "127.0.0.1" alongside whichever of "localhost" the first already answers for -- both are
/// loopback out of the box on every machine, so this needs no hosts-file edit and no header
/// spoofing. If SmStoreBaseUrl's host is somehow neither of the pair, the test still seeds a
/// second, distinct site explicitly (see SqlTestData.GetOrCreateSiteForHostAsync) rather than
/// silently passing over a database that happened to have only one.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class CrossTenantRefusalJourneyTests
{
    private readonly AspireAppFixture _fixture;

    public CrossTenantRefusalJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Customer_session_at_one_store_does_not_work_at_another()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var hostA = _fixture.SmStoreBaseUrl.Host;
        var hostB = hostA == "127.0.0.1" ? "localhost" : "127.0.0.1";

        var siteA = await SqlTestData.GetOrCreateSiteForHostAsync(_fixture.SmDatabaseConnectionString, hostA);
        var siteB = await SqlTestData.GetOrCreateSiteForHostAsync(_fixture.SmDatabaseConnectionString, hostB);

        siteA.Id.Should().NotBe(siteB.Id, "this journey needs two distinct stores to say anything at all");

        var customer = await IdentityTestSupport.CreateApprovedCustomerAsync(
            _fixture.ApiAuthConnectionString, _fixture.SmDatabaseConnectionString, siteA.SiteKey, siteA.Id);

        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        var storeAUri = _fixture.SmStoreBaseUrl;
        var storeBUri = new UriBuilder(_fixture.SmStoreBaseUrl) { Host = hostB }.Uri;

        await StoreFront.SignInAsync(page, storeAUri, customer.Email, customer.Password);

        await page.GotoAsync(new Uri(storeAUri, "/account").ToString());
        page.Url.Should().Contain("/account", "the session just signed in at its own store");

        var cookies = await context.CookiesAsync();
        var sessionCookie = cookies.Single(cookie => cookie.Name == CustomerAuthentication.CookieName);

        // AddCookiesAsync is Playwright's own cookie jar, not a request the app under test
        // ever saw -- it is what turns "the same person happened to browse both sites" into
        // the sharper "the exact bytes issued at store A arrive at store B", which is the
        // scenario CustomerSessionValidator exists for. A's own host-only cookie would never
        // travel to B on its own; the shared Data Protection ring is what makes B able to read
        // it at all once it does (see CLAUDE.md's remarks on AddSharedDataProtection).
        await context.AddCookiesAsync(
        [
            new Cookie
            {
                Name = sessionCookie.Name,
                Value = sessionCookie.Value,
                Domain = hostB,
                Path = "/",
                Secure = true,
                HttpOnly = true
            }
        ]);

        await page.GotoAsync(new Uri(storeBUri, "/account").ToString());
        page.Url.Should().Contain("/login",
            "CustomerSessionValidator resolves Contact -> Account -> Site for store B's id and " +
            "finds nothing, since this customer's Contact row belongs to store A -- " +
            "Account.razor redirects anyone that resolution rejects to /login");

        // And the original session is still exactly as good as it was: this refusal is
        // specific to store B, not a side effect that also burned the real one.
        await page.GotoAsync(new Uri(storeAUri, "/account").ToString());
        page.Url.Should().Contain("/account");
    }
}
