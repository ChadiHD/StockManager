using FluentAssertions;
using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 5: a customer who cannot remember their password gets back in — and whoever was
/// signed in as them does not.
/// </summary>
/// <remarks>
/// The second half is the part only an end-to-end run can prove. Resetting a password rotates
/// the login's security stamp in ApiAuthDb; the cookie sitting in another browser knows
/// nothing about that, and only a real cookie presented to a real running storefront shows
/// whether <c>CustomerSessionValidator</c> notices. A substituted user store cannot fail this
/// and neither can a bUnit render: the whole mechanism is a cookie, two databases and a
/// middleware ordering.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class PasswordResetJourneyTests
{
    private const string NewPassword = "E2e-Reset-Passw0rd!";

    private readonly AspireAppFixture _fixture;

    public PasswordResetJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Customer_resets_a_forgotten_password_and_every_older_session_ends()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var site = await SqlTestData.GetOrCreateSiteForHostAsync(
            _fixture.SmDatabaseConnectionString, _fixture.SmStoreBaseUrl.Host);

        var customer = await IdentityTestSupport.CreateApprovedCustomerAsync(
            _fixture.ApiAuthConnectionString, _fixture.SmDatabaseConnectionString,
            site.SiteKey, site.Id);

        // --- The session that is about to be invalidated ------------------------------
        // Signed in first, and deliberately in its own browser context: this stands for the
        // attacker's browser, or the customer's old laptop, and it is the thing the reset has
        // to reach without being able to see it.
        await using var oldSessionContext = await _fixture.NewContextAsync();
        var oldSession = await oldSessionContext.NewPageAsync();

        await StoreFront.SignInAsync(
            oldSession, _fixture.SmStoreBaseUrl, customer.Email, customer.Password);

        await oldSession.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, "/account").ToString());
        oldSession.Url.Should().Contain("/account", "this session is good before the reset");

        // --- The customer asks for a link --------------------------------------------
        await using var customerContext = await _fixture.NewContextAsync();
        var customerPage = await customerContext.NewPageAsync();

        await customerPage.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, "/forgot-password").ToString());
        await customerPage.Locator("#forgot-email").FillAsync(customer.Email);
        await customerPage.GetByRole(AriaRole.Button, new() { Name = "Email me a link" }).ClickAsync();

        // The acknowledgement is the same page an unknown address reaches, which is the point
        // of it; what this asserts is that the real form actually got that far.
        await Expect(customerPage.GetByRole(AriaRole.Heading, new() { Name = "Check your email" }))
            .ToBeVisibleAsync();

        // --- And follows it -----------------------------------------------------------
        var resetUrl = await IdentityTestSupport.PasswordResetUrlAsync(
            _fixture.ApiAuthConnectionString, _fixture.SmStoreBaseUrl, site.SiteKey, customer.Email);

        await customerPage.GotoAsync(resetUrl);
        await customerPage.Locator("#reset-password-new").FillAsync(NewPassword);
        await customerPage.Locator("#reset-password-confirm").FillAsync(NewPassword);
        await customerPage.GetByRole(AriaRole.Button, new() { Name = "Set new password" }).ClickAsync();

        await Expect(customerPage.Locator("h1")).ToHaveTextAsync("Password changed");

        // --- The new password works ----------------------------------------------------
        await StoreFront.SignInAsync(
            customerPage, _fixture.SmStoreBaseUrl, customer.Email, NewPassword);

        await customerPage.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, "/account").ToString());
        customerPage.Url.Should().Contain("/account");

        // --- And the old one is finished, session and all ------------------------------
        await oldSession.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, "/account").ToString());
        oldSession.Url.Should().Contain("/login",
            "the cookie was minted against the old security stamp, which ResetPasswordAsync " +
            "rotated -- without that check a customer resetting a password because somebody " +
            "is in their account would change nothing for the somebody");

        await oldSession.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, "/login").ToString());
        await oldSession.Locator("#signin-email").FillAsync(customer.Email);
        await oldSession.Locator("#signin-password").FillAsync(customer.Password);
        await oldSession.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        // Redisplayed with ?failed=1 rather than navigating away: the old password is gone.
        await oldSession.WaitForURLAsync(url => url.Contains("failed=1"));
    }
}
