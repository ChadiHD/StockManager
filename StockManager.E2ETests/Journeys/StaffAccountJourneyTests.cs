using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 10: an admin adds a colleague, who signs in with the password they were given and
/// replaces it.
/// </summary>
/// <remarks>
/// T7. Until then a user added from the portal could never sign in: the portal sent the API a
/// random password it never showed, and staff had no way to reset one. The hops are the portal's
/// form, the Admin-only Register, the staff-only /token, and the change-password endpoint that
/// takes the user from the token.
///
/// The colleague is made an Admin because the portal loads the admin snapshot for whoever signs
/// in; what this journey is about is the login, not what a lesser role can see.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class StaffAccountJourneyTests
{
    private const string FirstPassword = "First-Passw0rd!";
    private const string SecondPassword = "Second-Passw0rd!";

    private readonly AspireAppFixture _fixture;

    public StaffAccountJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Admin_adds_a_colleague_who_signs_in_and_changes_their_password()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var email = $"e2e-staff-{Guid.NewGuid():N}@example.test";

        await using var adminContext = await _fixture.NewContextAsync();
        var admin = await adminContext.NewPageAsync();

        await AdminPortal.SignInAsync(admin, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);
        await admin.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, "/admin/users").ToString());

        await admin.GetByRole(AriaRole.Button, new() { Name = "Add user" }).ClickAsync(new() { Timeout = 20_000 });

        var form = admin.Locator(".modal");
        // Tab after each fill: Blazor's InputText binds on change, which a person leaving the
        // box produces and fill() alone does not.
        await form.Locator("input").First.FillAsync("Ciara Walsh");
        await form.Locator("input").First.PressAsync("Tab");
        await form.Locator("input[type=email]").FillAsync(email);
        await form.Locator("input[type=email]").PressAsync("Tab");
        await form.Locator("select").SelectOptionAsync("Admin");
        await form.Locator("input[type=password]").FillAsync(FirstPassword);
        await form.Locator("input[type=password]").PressAsync("Tab");
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(admin.Locator(".toast")).ToContainTextAsync("User added", AdminPortal.AfterReload);

        // --- The colleague, in a browser of their own ----------------------------------------
        await using var staffContext = await _fixture.NewContextAsync();
        var staff = await staffContext.NewPageAsync();

        await AdminPortal.SignInAsync(staff, _fixture.SmPortalBaseUrl, email, FirstPassword);
        await staff.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, "/admin/password").ToString());

        await staff.Locator("#current-password").FillAsync(FirstPassword, new() { Timeout = 20_000 });
        await staff.Locator("#current-password").PressAsync("Tab");
        await staff.Locator("#new-password").FillAsync(SecondPassword);
        await staff.Locator("#new-password").PressAsync("Tab");
        await staff.Locator("#confirm-password").FillAsync(SecondPassword);
        await staff.Locator("#confirm-password").PressAsync("Tab");
        await staff.Locator("button.password-save").ClickAsync();

        await Expect(staff.Locator(".toast")).ToContainTextAsync("Password changed", AdminPortal.AfterReload);

        // The first password, which the admin also knows, no longer works; the new one does.
        // Asked of /token directly: a refused portal sign-in only shows as a twenty-second wait.
        using (var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        using (var http = new HttpClient(handler) { BaseAddress = _fixture.StockApiBaseUrl })
        {
            var stale = await http.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = email,
                ["password"] = FirstPassword
            }));

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, stale.StatusCode);
        }

        await using var laterContext = await _fixture.NewContextAsync();
        var later = await laterContext.NewPageAsync();

        await AdminPortal.SignInAsync(later, _fixture.SmPortalBaseUrl, email, SecondPassword);
    }
}
