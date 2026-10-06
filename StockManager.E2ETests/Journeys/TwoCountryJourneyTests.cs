using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 13: a UK store selling its own distributor's stock in pounds beside an Irish store
/// selling in euros, on one database. A UK customer pays 20% VAT, an Irish customer of the same
/// UK store is zero-rated as an export, and neither store sells the other's stock.
/// </summary>
/// <remarks>
/// T9's second-country exit in one pass. The hops are the ones the database tests cannot span:
/// dbo.fnSite_ProductSellable deciding what each storefront lists; the price formatted in the
/// store's own currency by SiteMoney; the quote request taking the store's currency rather than
/// the account's; the admin pricing it while acting for the UK store; uk-b2b assessing the
/// customer at acceptance inside the storefront; and the order storing GBP with the treatment
/// and legend.
///
/// Both stores map the same feed category, and the Irish store's own stock sits in it in euros:
/// the case that would have put the Irish product on the UK store with a pound sign on a euro
/// price. The suite has two names for loopback and other journeys own both stores, so the second
/// is borrowed as a UK store for this journey's length and put back afterwards, as
/// TaxAndConfirmationJourneyTests does with the tax rate.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class TwoCountryJourneyTests
{
    private readonly AspireAppFixture _fixture;

    public TwoCountryJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_UK_store_sells_its_own_stock_in_pounds_and_taxes_each_customer_by_where_they_are()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var db = _fixture.SmDatabaseConnectionString;
        var hostA = _fixture.SmStoreBaseUrl.Host;
        var hostB = hostA == "127.0.0.1" ? "localhost" : "127.0.0.1";
        var irishStore = _fixture.SmStoreBaseUrl;
        var ukStore = new UriBuilder(_fixture.SmStoreBaseUrl) { Host = hostB }.Uri;

        var siteA = await SqlTestData.GetOrCreateSiteForHostAsync(db, hostA);
        var siteB = await SqlTestData.GetOrCreateSiteForHostAsync(db, hostB);

        var previous = await SqlTestData.SetStoreTradingAsync(db, siteB.Id,
            new SqlTestData.StoreTrading("GB", "GBP", "en-GB", "uk-b2b", 20m));

        try
        {
            // The fixture shortens the storefront's site cache to a second; outlast it, so no page
            // below is served the euro store a previous journey cached.
            await Task.Delay(TimeSpan.FromSeconds(1.5));

            var ukCategory = await SqlTestData.CreateCategoryMappingAsync(db, siteB.Id);
            var irishCategory = await SqlTestData.CreateCategoryMappingAsync(db, siteA.Id, ukCategory.FeedValue);

            var unique = Guid.NewGuid().ToString("N")[..8];
            var ukSku = $"E2Euk-{unique}";
            var euroSku = $"E2Eeu-{unique}";

            await SqlTestData.InsertFeedProductAsync(db, siteB.Id, ukSku, $"UK item {unique}", ukCategory.FeedValue, 100m, 50);
            await SqlTestData.InsertProductAsync(db, euroSku, $"Euro item {unique}", ukCategory.FeedValue, 100m, 50);

            var british = await IdentityTestSupport.CreateApprovedCustomerAsync(
                _fixture.ApiAuthConnectionString, db, siteB.SiteKey, siteB.Id);
            await SqlTestData.SetAccountTaxIdentityAsync(db, british.Email, "GB", "GB123456789");

            var irish = await IdentityTestSupport.CreateApprovedCustomerAsync(
                _fixture.ApiAuthConnectionString, db, siteB.SiteKey, siteB.Id);
            await SqlTestData.SetAccountTaxIdentityAsync(db, irish.Email, "IE", "IE1234567T");

            // --- Each store lists only its own --------------------------------------------------
            await using var britishContext = await _fixture.NewContextAsync();
            var britishPage = await britishContext.NewPageAsync();
            await StoreFront.SignInAsync(britishPage, ukStore, british.Email, british.Password);

            await britishPage.GotoAsync(new Uri(ukStore, $"/catalog?cat={ukCategory.Slug}").ToString());
            await Expect(britishPage.Locator(".product-card")).ToHaveCountAsync(1);
            await Expect(britishPage.Locator(".product-card")).ToContainTextAsync($"UK item {unique}");
            await Expect(britishPage.Locator(".product-card__amount")).ToContainTextAsync("£100.00");

            await using var shopperContext = await _fixture.NewContextAsync();
            var shopper = await shopperContext.NewPageAsync();
            await shopper.GotoAsync(new Uri(irishStore, $"/catalog?cat={irishCategory.Slug}").ToString());
            await Expect(shopper.Locator(".product-card")).ToHaveCountAsync(1);
            await Expect(shopper.Locator(".product-card")).ToContainTextAsync($"Euro item {unique}");

            // --- Each customer asks the UK store for a quote ------------------------------------
            var britishQuote = await RequestQuoteAsync(britishPage, ukStore, ukCategory);

            await using var irishContext = await _fixture.NewContextAsync();
            var irishPage = await irishContext.NewPageAsync();
            await StoreFront.SignInAsync(irishPage, ukStore, irish.Email, irish.Password);
            var irishQuote = await RequestQuoteAsync(irishPage, ukStore, ukCategory);

            // --- An admin acting for the UK store sends both ------------------------------------
            await using var adminContext = await _fixture.NewContextAsync();
            var adminPage = await adminContext.NewPageAsync();
            await AdminPortal.SignInAsync(adminPage, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);
            await AdminPortal.EnsureActingForSiteAsync(adminPage, siteB.SiteKey);

            foreach (var reference in new[] { britishQuote, irishQuote })
            {
                await adminPage.GotoAsync(new Uri(_fixture.SmPortalBaseUrl, $"/admin/quotes/{reference}").ToString());
                await Expect(adminPage.Locator("button.quote-send")).ToBeEnabledAsync(new() { Timeout = 20_000 });
                await adminPage.Locator("button.quote-send").ClickAsync();
                await Expect(adminPage.Locator(".toast")).ToContainTextAsync("they can accept it now", AdminPortal.AfterReload);
            }

            // --- Each accepts, and is taxed by where they are ------------------------------------
            await AcceptAsync(britishPage, ukStore, britishQuote, "PO-GB-1");
            var britishSummary = britishPage.Locator(".document__summary");
            await Expect(britishSummary).ToContainTextAsync("Domestic standard");
            await Expect(britishSummary).ToContainTextAsync("£20.00");
            await Expect(britishSummary).ToContainTextAsync("£120.00");

            await AcceptAsync(irishPage, ukStore, irishQuote, "PO-IE-1");
            var irishSummary = irishPage.Locator(".document__summary");
            await Expect(irishSummary).ToContainTextAsync("Export");
            await Expect(irishSummary).ToContainTextAsync("£0.00");
            await Expect(irishSummary).ToContainTextAsync("Zero-rated export");

            // --- What each order stored --------------------------------------------------------
            var britishOrder = await ReadOrderAsync(britishQuote);
            britishOrder.Should().Be(new OrderRow("GBP", "Domestic standard", 20m, 120m));

            var irishOrder = await ReadOrderAsync(irishQuote);
            irishOrder.Should().Be(new OrderRow("GBP", "Export", 0m, 100m));
        }
        finally
        {
            await SqlTestData.SetStoreTradingAsync(db, siteB.Id, previous);
        }
    }

    /// <summary>Puts the category's only product in the basket and submits it. Returns the QT- reference.</summary>
    private static async Task<string> RequestQuoteAsync(IPage page, Uri store, SqlTestData.CategoryRecord category)
    {
        await page.GotoAsync(new Uri(store, $"/catalog?cat={category.Slug}").ToString());
        await page.Locator(".product-card__add").First.ClickAsync();
        await Expect(page.Locator(".site-header__count")).ToHaveTextAsync("1");

        await page.GotoAsync(new Uri(store, "/quote").ToString());
        await page.Locator("form.basket__submit button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/quote/submitted"));

        var reference = (await page.Locator(".submitted__reference").TextContentAsync())!.Trim();
        reference.Should().StartWith("QT-");

        return reference;
    }

    private static async Task AcceptAsync(IPage page, Uri store, string quoteReference, string poNumber)
    {
        await page.GotoAsync(new Uri(store, $"/account/quotes/{quoteReference}").ToString());
        await page.Locator("form.document__accept input[name=poNumber]").FillAsync(poNumber);
        await page.Locator("form.document__accept button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/account/orders/SO-"));
    }

    private sealed record OrderRow(string Currency, string? TaxTreatment, decimal Vat, decimal Total);

    private async Task<OrderRow> ReadOrderAsync(string quoteReference)
    {
        await using var connection = new SqlConnection(_fixture.SmDatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT p.[Currency], p.[TaxTreatment], p.[VAT], p.[FinalPrice]
            FROM dbo.Purchase p
            INNER JOIN dbo.Quote q ON q.[Id] = p.[QuoteId]
            WHERE q.[Reference] = @Reference;
            """, connection);
        command.Parameters.AddWithValue("@Reference", quoteReference);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new OrderRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetDecimal(2), reader.GetDecimal(3));
    }
}
