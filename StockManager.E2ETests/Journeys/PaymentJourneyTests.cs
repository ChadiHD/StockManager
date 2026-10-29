using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 16: an admin gives a checkout store its Stripe keys and a delivery rate, a guest pays,
/// and only the signed webhook says so (T13).
/// </summary>
/// <remarks>
/// <para>The hops no unit test spans:</para>
/// <list type="bullet">
/// <item>the keys typed into the portal, encrypted by one host and decrypted by the other;</item>
/// <item>the delivery rate, charged at checkout and added to the session's total;</item>
/// <item>the checkout's redirect to the hosted page, which the storefront's CSP must allow;</item>
/// <item>a webhook refused unsigned and applied signed, once, however often it arrives;</item>
/// <item>the order page waiting on the webhook rather than the return;</item>
/// <item>the tracking number the portal sends, and the mail it queues.</item>
/// </list>
/// Stripe is <see cref="StripeStub"/>. Everything on the storefront's side is real.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class PaymentJourneyTests
{
    private readonly AspireAppFixture _fixture;

    public PaymentJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_guest_pays_and_only_the_signed_webhook_marks_the_order_paid()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var db = _fixture.SmDatabaseConnectionString;
        var store = _fixture.SmStoreBaseUrl;
        var site = await SqlTestData.GetOrCreateSiteForHostAsync(db, store.Host);
        var category = await SqlTestData.CreateCategoryMappingAsync(db, site.Id);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var sku = $"E2Epay-{unique}";
        var webhookSecret = $"whsec_journey_{unique}";
        await SqlTestData.InsertProductAsync(db, sku, $"Paid shirt {unique}", category.FeedValue, 40m, 2);

        // Borrowed for this journey's length, as the checkout journey borrows it, and left as it was
        // found: the checkout journey expects a store that takes no payment and charges no delivery.
        var previous = await SqlTestData.SetOrderModeAsync(db, site.Id, "DirectCheckout");

        try
        {
            // --- The admin sets the store up ---------------------------------------------------
            await using var adminContext = await _fixture.NewContextAsync();
            var admin = await adminContext.NewPageAsync();

            await AdminPortal.SignInAsync(admin, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);
            await AdminPortal.EnsureActingForSiteAsync(admin, site.SiteKey);

            await admin.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, "/admin/store").ToString());
            await Expect(admin.Locator(".store-payment-status")).ToContainTextAsync("No keys", AdminPortal.AfterReload);
            await TypeAsync(admin, "#store-payment-secret", "sk_test_journey");
            await TypeAsync(admin, "#store-payment-webhook", webhookSecret);
            await admin.Locator(".store-payment-save").ClickAsync();
            await Expect(admin.Locator(".store-payment-status")).ToContainTextAsync("Keys are set", AdminPortal.AfterReload);

            await admin.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, "/admin/shipping").ToString());
            await Expect(admin.Locator(".shipping-add")).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await TypeAsync(admin, "#shipping-name", $"Standard {unique}");
            await TypeAsync(admin, "#shipping-countries", "BR");
            await TypeAsync(admin, "#shipping-price", "10");
            await admin.Locator(".shipping-save").ClickAsync();
            await Expect(admin.Locator(".shipping-table")).ToContainTextAsync($"Standard {unique}", AdminPortal.AfterReload);

            // The fixture's site cache is a second long.
            await Task.Delay(1_500);

            // --- A guest checks out, and is sent to the hosted page ----------------------------
            await using var guestContext = await _fixture.NewContextAsync();
            var guest = await guestContext.NewPageAsync();

            await guest.GotoAsync(new Uri(store, $"/product/{sku}").ToString());
            await guest.Locator("form.product__add button[type=submit]").ClickAsync();
            await guest.GotoAsync(new Uri(store, "/checkout").ToString());

            // Nowhere the store delivers: refused on the field, and nothing placed.
            await FillCheckoutAsync(guest, $"guest-{unique}@example.test", "PT");
            await Expect(guest.Locator(".validation-message")).ToContainTextAsync("We do not deliver to Portugal");

            await FillCheckoutAsync(guest, $"guest-{unique}@example.test", "BR");
            await Expect(guest).ToHaveURLAsync(new Regex("^" + Regex.Escape(_fixture.Stripe.BaseUrl + "pay/cs_test_")),
                new() { Timeout = 20_000 });

            var session = _fixture.Stripe.Sessions.Single(candidate => candidate.SuccessUrl.Contains(store.Authority)
                                                                       && guest.Url.EndsWith(candidate.Id));
            session.Authorization.Should().Be("Bearer sk_test_journey", "the portal's ciphertext reached the storefront as the key");

            var order = await ReadOrderAsync(session.Reference);
            order.ShippingName.Should().Be($"Standard {unique}");
            order.ShippingPrice.Should().Be(10m);
            session.TotalMinor.Should().Be((long)(order.FinalPrice * 100), "the hosted page charges exactly what the order says");

            // --- Back from paying: the return proves nothing -----------------------------------
            await guest.GotoAsync(session.SuccessUrl);
            await Expect(guest.Locator(".document__notice")).ToContainTextAsync("We are confirming your payment");
            await Expect(guest.Locator(".page-header .tag")).ToHaveTextAsync("Awaiting payment");

            // --- The webhook: refused unsigned, applied signed, once ----------------------------
            var webhook = new Uri(store, "/webhooks/stripe").ToString();
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                id = $"evt_{unique}",
                type = "checkout.session.completed",
                data = new { @object = new { id = session.Id, client_reference_id = session.Reference, payment_status = "paid" } }
            });

            (await PostAsync(guest, webhook, payload, StripeStub.Signature(payload, "whsec_not_this_store"))).Should().Be(400);
            (await ReadOrderAsync(session.Reference)).Status.Should().Be("Awaiting payment");

            (await PostAsync(guest, webhook, payload, StripeStub.Signature(payload, webhookSecret))).Should().Be(200);
            (await PostAsync(guest, webhook, payload, StripeStub.Signature(payload, webhookSecret))).Should().Be(200);

            order = await ReadOrderAsync(session.Reference);
            order.Status.Should().Be("Paid");
            order.Events.Should().Be(1, "a replayed event is recorded once");
            order.PaidMail.Should().Be(1, "and mailed once");

            await guest.ReloadAsync();
            await Expect(guest.Locator(".page-header .tag")).ToHaveTextAsync("Paid");
            await Expect(guest.Locator(".document__notice")).ToContainTextAsync("Your payment is confirmed");

            // --- The admin ships it ------------------------------------------------------------
            await admin.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, $"/admin/orders/{session.Reference}").ToString());
            await TypeAsync(admin, "#order-tracking", $"BR{unique}");
            await admin.Locator(".head-actions .btn-primary").ClickAsync();
            await Expect(admin.Locator(".detail-title .badge")).ToHaveTextAsync("Fulfilled", new() { Timeout = 20_000 });

            order = await ReadOrderAsync(session.Reference);
            order.TrackingNumber.Should().Be($"BR{unique}");
            order.ShippedMail.Should().Be(1);
        }
        finally
        {
            await SqlTestData.SetOrderModeAsync(db, site.Id, previous);
            await RemovePaymentSetupAsync(site.Id);
        }
    }

    /// <summary>Each portal input commits on change, which a fill alone does not drive; Tab does.</summary>
    private static async Task TypeAsync(IPage page, string selector, string value)
    {
        await page.Locator(selector).FillAsync(value);
        await page.Locator(selector).PressAsync("Tab");
    }

    private static async Task FillCheckoutAsync(IPage page, string email, string country)
    {
        await page.Locator("#checkout-email").FillAsync(email);
        await page.Locator("#checkout-firstname").FillAsync("Ana");
        await page.Locator("#checkout-lastname").FillAsync("Silva");
        await page.Locator("#checkout-line1").FillAsync("Rua Augusta, 10");
        await page.Locator("#checkout-city").FillAsync("São Paulo");
        await page.Locator("#checkout-country").SelectOptionAsync(country);
        await page.Locator(".checkout__place").ClickAsync();
    }

    private static async Task<int> PostAsync(IPage page, string url, string payload, string signature)
    {
        var response = await page.APIRequest.PostAsync(url, new()
        {
            DataString = payload,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Stripe-Signature"] = signature
            }
        });

        return response.Status;
    }

    private sealed record OrderRow(
        string Status, string? ShippingName, decimal ShippingPrice, decimal FinalPrice, string? TrackingNumber,
        int Events, int PaidMail, int ShippedMail);

    private async Task<OrderRow> ReadOrderAsync(string reference)
    {
        await using var connection = new SqlConnection(_fixture.SmDatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT p.[Status], p.[ShippingName], p.[ShippingPrice], p.[FinalPrice], p.[TrackingNumber],
                   (SELECT COUNT(*) FROM dbo.PaymentEvent e WHERE e.[Reference] = p.[Reference] AND e.[SiteId] = p.[SiteId]),
                   (SELECT COUNT(*) FROM dbo.EmailOutbox o WHERE o.[TemplateKey] = N'order.paid' AND o.[ToAddress] = c.[Email]),
                   (SELECT COUNT(*) FROM dbo.EmailOutbox o WHERE o.[TemplateKey] = N'order.shipped' AND o.[ToAddress] = c.[Email])
            FROM dbo.Purchase p
            INNER JOIN dbo.Contact c ON c.[Id] = p.[PlacedByContactId]
            WHERE p.[Reference] = @Reference;
            """, connection);

        command.Parameters.AddWithValue("@Reference", reference);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        return new OrderRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetDecimal(2),
            reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7));
    }

    /// <summary>
    /// Puts the store back as the other journeys expect it: no keys, and no delivery rate. By SQL,
    /// because the portal's screens rightly never offer to hand the keys back.
    /// </summary>
    private async Task RemovePaymentSetupAsync(int siteId)
    {
        await using var connection = new SqlConnection(_fixture.SmDatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            UPDATE dbo.Site SET [PaymentSecretRef] = NULL, [PaymentWebhookSecretRef] = NULL WHERE [Id] = @SiteId;
            UPDATE dbo.ShippingRate SET [IsActive] = 0 WHERE [SiteId] = @SiteId;
            """, connection);

        command.Parameters.AddWithValue("@SiteId", siteId);
        await command.ExecuteNonQueryAsync();
    }
}
