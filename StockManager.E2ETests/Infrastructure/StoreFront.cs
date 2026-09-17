using Microsoft.Playwright;

namespace StockManager.E2ETests.Infrastructure;

/// <summary>
/// The handful of SMStore interactions more than one journey needs.
/// </summary>
public static class StoreFront
{
    public static async Task SignInAsync(IPage page, Uri baseUrl, string email, string password)
    {
        await page.GotoAsync(new Uri(baseUrl, "/login").ToString());
        await page.Locator("#signin-email").FillAsync(email);
        await page.Locator("#signin-password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        // A failed attempt redisplays /login with ?failed=1 rather than navigating away (see
        // CustomerAuthEndpoints.Failed), so waiting for the URL to leave /login is what tells
        // a genuine sign-in apart from one CustomerAuthEndpoints quietly refused.
        await page.WaitForURLAsync(url => !url.Contains("/login"));
    }
}
