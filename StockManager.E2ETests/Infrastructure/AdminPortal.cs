using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Infrastructure;

/// <summary>
/// The handful of SMPortal interactions more than one journey needs.
/// </summary>
public static class AdminPortal
{
    public static async Task SignInAsync(IPage page, Uri baseUrl, string email, string password)
    {
        /*
        The token call is recorded because the portal cannot report it.

        SMPortal is WebAssembly: AuthenticationService.Login turns any non-2xx into a null and
        Login.razor renders "Check your email and password", losing the status code and the
        body. When the same credentials succeed against /token from a .NET client and fail from
        the browser, that difference is the whole diagnosis, and without this it is invisible.
        */
        var tokenCalls = new List<string>();

        page.Response += async (_, response) =>
        {
            if (!response.Url.Contains("/token", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string body;
            try { body = await response.TextAsync(); } catch { body = "<unreadable>"; }

            tokenCalls.Add($"{response.Status} {response.Url} -> {body[..Math.Min(body.Length, 300)]}");
        };

        await page.GotoAsync(new Uri(baseUrl, "/login").ToString());
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in securely" }).ClickAsync();

        /*
        Login.razor navigates away with NavigationManager on success and stays put (with an
        error banner) on failure, so the URL leaving /login is what tells the two apart --
        assuming the click alone succeeded would fail later, somewhere unrelated.

        The timeout is caught and rethrown with what is actually on the screen. Playwright's
        own message for this is "Timeout 30000ms exceeded. waiting for navigation until Load",
        which says nothing about why the portal refused the credentials, and a sign-in that
        fails here fails every admin journey in the suite at once.
        */
        try
        {
            await page.WaitForURLAsync(url => !url.Contains("/login"), new() { Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
            var shown = await page.Locator("body").InnerTextAsync();

            throw new InvalidOperationException(
                $"The admin portal stayed on {page.Url} after signing in as {email}. " +
                "That is a refused sign-in, not a slow one.\n" +
                $"Token calls the browser made: {(tokenCalls.Count == 0
                    ? "none — the request never left the page, so look at the API address in " +
                      "SMPortal/wwwroot/appsettings.json and at CORS"
                    : string.Join("\n  ", tokenCalls))}\n" +
                "What the page showed:\n" + shown[..Math.Min(shown.Length, 400)]);
        }
    }

    /// <summary>
    /// Makes sure the admin session is acting for <paramref name="siteKey"/> before the caller
    /// does anything that AdminSiteResolutionMiddleware scopes by it (accounts, groups, quotes,
    /// orders, feeds -- not products, which carry no SiteId at all).
    /// </summary>
    /// <remarks>
    /// AdminLayout.razor only renders the store switcher once Data.Sites.Count > 1, so on a
    /// database that still has only the seeded site there is nothing to switch and nothing to
    /// wait for. And AdminLayout's own OnSiteChanged short-circuits before showing a toast when
    /// asked to switch to the store that is already selected, so this checks the switcher's
    /// current value first rather than unconditionally selecting and waiting for a toast that,
    /// on a no-op switch, is never coming.
    /// </remarks>
    public static async Task EnsureActingForSiteAsync(IPage page, string siteKey)
    {
        var switcher = page.Locator("#admin-site");

        if (await switcher.CountAsync() == 0)
        {
            return;
        }

        if (await switcher.InputValueAsync() == siteKey)
        {
            return;
        }

        await switcher.SelectOptionAsync([new SelectOptionValue { Value = siteKey }]);
        await Expect(page.Locator(".toast")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The input, select or textarea inside the ".field" that contains
    /// <paramref name="labelText"/>.
    /// </summary>
    /// <remarks>
    /// Most of the admin's own forms (ProductDetail.razor's "Available units",
    /// Groups.razor's "Base discount (%)") render a bare &lt;label&gt; as the input's sibling
    /// rather than its wrapper and set no `for`, so Playwright's GetByLabel -- which needs one
    /// or the other -- cannot find them. This is the structural lookup that survives that.
    /// </remarks>
    public static ILocator FieldControl(this IPage page, string labelText) =>
        page.Locator($".field:has-text(\"{labelText}\")").Locator("input, select, textarea");

    public static ILocator FieldControl(this ILocator scope, string labelText) =>
        scope.Locator($".field:has-text(\"{labelText}\")").Locator("input, select, textarea");
}
