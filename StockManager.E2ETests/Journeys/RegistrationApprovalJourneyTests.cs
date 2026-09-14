using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 3: a stranger applies on the storefront, an admin approves the application with a
/// customer group, and the applicant signs in and sees that group's prices.
/// </summary>
/// <remarks>
/// End to end through the real UI on both sides: /register (with a real file upload), the
/// admin's pending-application screen, its approval modal, and /login -- nothing here is
/// short-circuited through the API or the database. CrossTenantRefusalJourneyTests is the one
/// that provisions a customer directly, precisely because it is testing something else.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class RegistrationApprovalJourneyTests
{
    private static readonly Regex ReferencePattern = new("AC-\\d+", RegexOptions.Compiled);

    private readonly AspireAppFixture _fixture;

    public RegistrationApprovalJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stranger_applies_is_approved_with_a_group_and_then_signs_in_to_see_group_prices()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var site = await SqlTestData.GetOrCreateSiteForHostAsync(
            _fixture.SmDatabaseConnectionString, _fixture.SmStoreBaseUrl.Host);

        var category = await SqlTestData.CreateCategoryMappingAsync(_fixture.SmDatabaseConnectionString, site.Id);
        var sku = $"E2E-{Guid.NewGuid():N}"[..20];

        // MinMarginPct is 0 on every site GetOrCreateSiteForHostAsync can hand back (Seed.sql's
        // row and this project's own both default it), and this product's Cost is left null,
        // so PriceResolver's margin floor cannot bind -- the discount below is arithmetic on
        // list price and nothing else: 100.00 * (1 - 10%) = 90.00, no rounding ambiguity.
        const decimal retailPrice = 100.00m;
        const int groupDiscountPct = 10;

        await SqlTestData.InsertProductAsync(
            _fixture.SmDatabaseConnectionString, sku, "E2E group pricing widget",
            category.FeedValue, retailPrice, quantityInStock: 10);

        var unique = Guid.NewGuid().ToString("N")[..10];
        var email = $"e2e-applicant-{unique}@example.test";
        const string password = "E2e-Test-Passw0rd!";
        var groupName = $"E2E group {unique}";

        await using var customerContext = await _fixture.NewContextAsync();
        var customerPage = await customerContext.NewPageAsync();

        // The anonymous price, captured before any of the rest of this exists, so "the member
        // price differs" below is a comparison against something this run actually observed --
        // not an assumption about how the anonymous figure would have been formatted.
        var productUrl = new Uri(_fixture.SmStoreBaseUrl, $"/product/{sku}").ToString();
        await customerPage.GotoAsync(productUrl);
        var anonymousPrice = await customerPage.Locator(".product__amount").InnerTextAsync();

        // --- A stranger applies -----------------------------------------------------
        await customerPage.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, "/register").ToString());

        // Field ids follow "reg-{RegistrationField}" (SMStore/Components/Pages/Register.razor);
        // this store's field set is eu-b2b (Scripts/PostDeployment/Seed.sql and
        // SqlTestData.GetOrCreateSiteForHostAsync both default RegistrationFieldSet to it),
        // whose required fields are exactly the ones filled in below
        // (EuB2bRegistrationFieldSet.Rules) plus the Chamber of Commerce document.
        await customerPage.Locator("#reg-company").FillAsync($"E2E Applicant Co {unique}");
        await customerPage.Locator("#reg-vatnumber").FillAsync("IE1234567X");
        await customerPage.Locator("#reg-firstname").FillAsync("Ada");
        await customerPage.Locator("#reg-lastname").FillAsync("Lovelace");
        await customerPage.Locator("#reg-email").FillAsync(email);
        await customerPage.Locator("#reg-phone").FillAsync("+353 1 234 5678");
        await customerPage.Locator("#reg-addressline1").FillAsync("1 Test Street");
        await customerPage.Locator("#reg-city").FillAsync("Dublin");
        // #reg-country is left on its default: Register.razor pre-selects the site's own
        // Country (IE for every site this project creates), which keeps the VAT prefix above
        // from tripping EuB2bRegistrationFieldSet's cross-border advisory.
        await customerPage.Locator("#reg-password").FillAsync(password);
        await customerPage.Locator("#reg-confirm").FillAsync(password);

        await customerPage.Locator("#doc-ChamberOfCommerce").SetInputFilesAsync(new FilePayload
        {
            Name = "certificate.pdf",
            MimeType = "application/pdf",
            Buffer = TinyPdf.Bytes
        });

        await customerPage.GetByRole(AriaRole.Button, new() { Name = "Submit application" }).ClickAsync();

        await Expect(customerPage.GetByRole(AriaRole.Heading, new() { Name = "Application received" }))
            .ToBeVisibleAsync();

        var acknowledgement = await customerPage.Locator("p.page-lede").InnerTextAsync();
        var reference = ReferencePattern.Match(acknowledgement).Value;
        reference.Should().NotBeEmpty("the acknowledgement page states the application's own AC-nnnn reference");

        // --- An admin decides it -----------------------------------------------------
        await using var adminContext = await _fixture.NewContextAsync();
        var adminPage = await adminContext.NewPageAsync();

        await AdminPortal.SignInAsync(adminPage, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);
        // Unlike products, CustomerGroupController and AccountController both resolve
        // IAdminSiteContext from X-Site-Key -- spAccount_Approve throws if the chosen group's
        // SiteId does not match the account's, so the admin session has to be acting for this
        // applicant's own site before either step below.
        await AdminPortal.EnsureActingForSiteAsync(adminPage, site.SiteKey);

        await adminPage.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, "/admin/groups").ToString());
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "New group" }).ClickAsync();
        await adminPage.Locator(".modal").FieldControl("Group name").FillAsync(groupName);
        await adminPage.Locator(".modal").FieldControl("Base discount").FillAsync(groupDiscountPct.ToString());
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Create group" }).ClickAsync();
        await Expect(adminPage.Locator(".toast")).ToHaveTextAsync("Customer group created");

        // Accounts.razor and AccountDetail.razor both key on the human AC-nnnn reference
        // rather than a database id (CLAUDE.md: "The UI models key on human references"), so
        // the reference the acknowledgement page gave the applicant is a real deep link.
        await adminPage.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, $"/admin/accounts/{reference}").ToString());
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Approve account" }).ClickAsync();
        await adminPage.Locator(".modal select").SelectOptionAsync([new SelectOptionValue { Label = groupName }]);
        // Scoped to .modal-foot: the pending-application banner behind the now-open modal has
        // its own "Approve account" button with the same accessible name, still in the DOM.
        await adminPage.Locator(".modal-foot").GetByRole(AriaRole.Button, new() { Name = "Approve account" }).ClickAsync();
        await Expect(adminPage.Locator(".toast")).ToHaveTextAsync("Account approved");

        // --- The applicant signs in and sees the group's terms ------------------------
        await StoreFront.SignInAsync(customerPage, _fixture.SmStoreBaseUrl, email, password);

        await customerPage.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, "/account").ToString());
        await Expect(customerPage.Locator("p.page-lede")).ToContainTextAsync($"{groupDiscountPct}% off list");

        await customerPage.GotoAsync(productUrl);
        var memberPrice = await customerPage.Locator(".product__amount").InnerTextAsync();

        memberPrice.Should().NotBe(anonymousPrice);
        memberPrice.Should().Contain("90.00");
    }
}
