using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 8: two customers buy the same thing from the same store and are charged differently,
/// for a reason each of them can read, and every message the round trip owes them is sent.
/// </summary>
/// <remarks>
/// T6's exit criterion in one pass. A German company with a VAT number and an Irish one without
/// each ask for a quote through the storefront; an admin sends both from the portal; each
/// accepts their own. The German order carries no VAT and the Article 196 legend, the Irish one
/// carries 23%, and both say why on the page the customer lands on.
///
/// The hops this spans are the ones no unit test can: the account's country and VAT number,
/// read in the storefront and handed to <c>ITaxRuleSet</c> in C#; the rate reaching only taxable
/// lines inside <c>spOrder_ConvertFromQuote</c>; the snapshot read back onto the order page; and
/// every message — request received, quote priced, order confirmed — queued by the procedure
/// that made the change and then claimed, rendered and handed to the transport by the
/// dispatcher in the other host. "Sent" in the outbox is the dispatcher's word that all of that
/// happened; a template that failed to render would leave the row Pending with its error.
///
/// The store's rate is set for the journey and put back afterwards. It lives on <c>Site</c>,
/// which has no admin screen, and the seeded store charges nothing until somebody sets one.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class TaxAndConfirmationJourneyTests
{
    private const decimal IrishStandardRate = 23m;

    /// <summary>How long the dispatcher has to send a message. It polls every five seconds.</summary>
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(45);

    private readonly AspireAppFixture _fixture;

    public TaxAndConfirmationJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tax_turns_on_the_customer_and_every_message_is_sent()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var db = _fixture.SmDatabaseConnectionString;
        var site = await SqlTestData.GetOrCreateSiteForHostAsync(db, _fixture.SmStoreBaseUrl.Host);
        var previousRate = await SqlTestData.SetStandardTaxRateAsync(db, site.Id, IrishStandardRate);

        try
        {
            var category = await SqlTestData.CreateCategoryMappingAsync(db, site.Id);
            var sku = $"E2Etax{Guid.NewGuid():N}"[..12];

            await SqlTestData.InsertProductAsync(db, sku, $"{sku} item", category.FeedValue, 100m, 50);

            var german = await IdentityTestSupport.CreateApprovedCustomerAsync(
                _fixture.ApiAuthConnectionString, db, site.SiteKey, site.Id);
            await SqlTestData.SetAccountTaxIdentityAsync(db, german.Email, "DE", "DE123456789");

            var irish = await IdentityTestSupport.CreateApprovedCustomerAsync(
                _fixture.ApiAuthConnectionString, db, site.SiteKey, site.Id);
            await SqlTestData.SetAccountTaxIdentityAsync(db, irish.Email, "IE", vatNumber: null);

            // --- Each asks for a quote on the same catalog ------------------------------------
            await using var germanContext = await _fixture.NewContextAsync();
            var germanPage = await germanContext.NewPageAsync();
            var germanQuote = await RequestQuoteAsync(germanPage, german, category);

            await using var irishContext = await _fixture.NewContextAsync();
            var irishPage = await irishContext.NewPageAsync();
            var irishQuote = await RequestQuoteAsync(irishPage, irish, category);

            // A quote states no tax: it turns on who accepts it, at the moment they do.
            await germanPage.GotoAsync(StorePage($"/account/quotes/{germanQuote}"));
            await Expect(germanPage.Locator(".document__summary"))
                .ToContainTextAsync("Excluding tax and freight, which are calculated on the order.");

            // --- An admin sends both ------------------------------------------------------------
            await using var adminContext = await _fixture.NewContextAsync();
            var adminPage = await adminContext.NewPageAsync();

            await AdminPortal.SignInAsync(
                adminPage, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);
            await AdminPortal.EnsureActingForSiteAsync(adminPage, site.SiteKey);

            foreach (var reference in new[] { germanQuote, irishQuote })
            {
                await adminPage.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, $"/admin/quotes/{reference}").ToString());

                // 20 seconds: each GotoAsync is a cold WebAssembly boot. See QuoteRequestJourneyTests.
                await Expect(adminPage.Locator("button.quote-send")).ToBeEnabledAsync(new() { Timeout = 20_000 });
                await adminPage.Locator("button.quote-send").ClickAsync();
                await Expect(adminPage.Locator(".toast")).ToContainTextAsync("they can accept it now");
            }

            // --- Each accepts their own ---------------------------------------------------------
            await AcceptAsync(germanPage, germanQuote, "PO-DE-1");

            // Labelled by treatment, with the reason in writing, rather than "VAT €0.00".
            var germanSummary = germanPage.Locator(".document__summary");
            await Expect(germanSummary).ToContainTextAsync("Intra-EU reverse charge");
            await Expect(germanSummary).ToContainTextAsync("€0.00");
            await Expect(germanSummary).ToContainTextAsync("Article 196");

            await AcceptAsync(irishPage, irishQuote, "PO-IE-1");

            var irishSummary = irishPage.Locator(".document__summary");
            await Expect(irishSummary).ToContainTextAsync("Domestic standard");
            await Expect(irishSummary).ToContainTextAsync("€23.00");
            await Expect(irishSummary).ToContainTextAsync("€123.00");

            // --- What each order stored -------------------------------------------------------
            var germanOrder = await ReadOrderAsync(germanQuote);
            germanOrder.TaxTreatment.Should().Be("Intra-EU reverse charge");
            germanOrder.Vat.Should().Be(0m);
            germanOrder.Total.Should().Be(100m);
            germanOrder.LineRatePct.Should().Be(0m);

            var irishOrder = await ReadOrderAsync(irishQuote);
            irishOrder.TaxTreatment.Should().Be("Domestic standard");
            irishOrder.Vat.Should().Be(23m);
            irishOrder.Total.Should().Be(123m);
            // Per line, so a store that later sells a reduced-rate product needs no new column.
            irishOrder.LineRatePct.Should().Be(IrishStandardRate);

            // --- And every message the round trip owed, sent --------------------------------
            foreach (var customer in new[] { german, irish })
            {
                foreach (var template in new[] { "quote.received", "quote.priced", "order.confirmed" })
                {
                    var message = await WaitForDispatchAsync(template, customer.Email);

                    message.Status.Should().Be("Sent",
                        $"the dispatcher should have sent {template} to {customer.Email}; " +
                        $"its last error was: {message.LastError ?? "none"}");
                }
            }

            // The confirmation carries the order's own legend, so the German buyer's accountant
            // gets the Article 196 statement in writing as well as on the page.
            var germanConfirmation = await WaitForDispatchAsync("order.confirmed", german.Email);
            using var payload = JsonDocument.Parse(germanConfirmation.PayloadJson!);
            payload.RootElement.GetProperty("reference").GetString().Should().Be(germanOrder.Reference);
            payload.RootElement.GetProperty("taxLegend").GetString().Should().Contain("Article 196");
        }
        finally
        {
            await SqlTestData.SetStandardTaxRateAsync(db, site.Id, previousRate);
        }
    }

    private string StorePage(string path) => new Uri(_fixture.SmStoreBaseUrl, path).ToString();

    /// <summary>Signs in, puts the product in the basket and submits it. Returns the QT- reference.</summary>
    private async Task<string> RequestQuoteAsync(
        IPage page, IdentityTestSupport.Credentials customer, SqlTestData.CategoryRecord category)
    {
        await StoreFront.SignInAsync(page, _fixture.SmStoreBaseUrl, customer.Email, customer.Password);

        await page.GotoAsync(StorePage($"/catalog?cat={category.Slug}"));
        await page.Locator(".product-card__add").First.ClickAsync();
        await Expect(page.Locator(".site-header__count")).ToHaveTextAsync("1");

        await page.GotoAsync(StorePage("/quote"));
        await page.Locator("form.basket__submit button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/quote/submitted"));

        var reference = (await page.Locator(".submitted__reference").TextContentAsync())!.Trim();
        reference.Should().StartWith("QT-");

        return reference;
    }

    private async Task AcceptAsync(IPage page, string quoteReference, string poNumber)
    {
        await page.GotoAsync(StorePage($"/account/quotes/{quoteReference}"));
        await page.Locator("form.document__accept input[name=poNumber]").FillAsync(poNumber);
        await page.Locator("form.document__accept button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/account/orders/SO-"));
    }

    private sealed record OrderRow(string Reference, string? TaxTreatment, decimal Vat, decimal Total, decimal LineRatePct);

    private async Task<OrderRow> ReadOrderAsync(string quoteReference)
    {
        await using var connection = new SqlConnection(_fixture.SmDatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT p.[Reference], p.[TaxTreatment], p.[VAT], p.[FinalPrice], MAX(d.[TaxRatePct])
            FROM dbo.Purchase p
            INNER JOIN dbo.Quote q ON q.[Id] = p.[QuoteId]
            INNER JOIN dbo.PurchaseDetail d ON d.[PurchaseId] = p.[Id]
            WHERE q.[Reference] = @Reference
            GROUP BY p.[Reference], p.[TaxTreatment], p.[VAT], p.[FinalPrice];
            """, connection);

        command.Parameters.AddWithValue("@Reference", quoteReference);

        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue($"{quoteReference} should have become an order");

        return new OrderRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4));
    }

    private sealed record OutboxRow(string Status, string? PayloadJson, string? LastError);

    /// <summary>
    /// The newest message of a kind to one address, once the dispatcher has sent it — or as it
    /// stands when the time runs out, so a failure says what the dispatcher last recorded.
    /// </summary>
    private async Task<OutboxRow> WaitForDispatchAsync(string templateKey, string toAddress)
    {
        var deadline = DateTime.UtcNow + DispatchTimeout;
        OutboxRow? latest = null;

        while (DateTime.UtcNow < deadline)
        {
            await using var connection = new SqlConnection(_fixture.SmDatabaseConnectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand(
                """
                SELECT TOP (1) [Status], [PayloadJson], [LastError]
                FROM dbo.EmailOutbox
                WHERE [TemplateKey] = @TemplateKey AND [ToAddress] = @ToAddress
                ORDER BY [Id] DESC;
                """, connection);

            command.Parameters.AddWithValue("@TemplateKey", templateKey);
            command.Parameters.AddWithValue("@ToAddress", toAddress);

            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    latest = new OutboxRow(
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2));

                    if (latest.Status == "Sent")
                    {
                        return latest;
                    }
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        latest.Should().NotBeNull($"{templateKey} to {toAddress} should have been queued at all");

        return latest!;
    }
}
