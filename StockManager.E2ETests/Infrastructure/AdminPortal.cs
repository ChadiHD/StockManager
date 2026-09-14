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
        await page.GotoAsync(new Uri(baseUrl, "/login").ToString());
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in securely" }).ClickAsync();

        // Login.razor navigates away with NavigationManager on success and stays put (with an
        // error banner) on failure -- waiting for the URL is what tells the two apart, rather
        // than assuming the click alone means the sign-in succeeded.
        await page.WaitForURLAsync(url => !url.Contains("/login"));
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
