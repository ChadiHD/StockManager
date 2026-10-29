using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 15: two guests want the last shirt on a checkout store (T12). The first buys it; the
/// second is told, and nothing is written for them.
/// </summary>
/// <remarks>
/// <para>The hops no unit test spans:</para>
/// <list type="bullet">
/// <item>the store's mode, read per request, turning the basket into a cart that leads to checkout;</item>
/// <item>the checkout form, posted back to itself through static SSR and antiforgery;</item>
/// <item>spOrder_PlaceFromBasket writing a guest's account, the order, its address and the stock;</item>
/// <item>the redirect to a page only the token opens, from any browser;</item>
/// <item>the outbox row the storefront queued, encrypted, for the other host to send;</item>
/// <item>a second cart for the same shirt, refused by the lock rather than oversold.</item>
/// </list>
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class DirectCheckoutJourneyTests
{
    private readonly AspireAppFixture _fixture;

    public DirectCheckoutJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Two_guests_want_the_last_shirt_and_one_gets_it()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var db = _fixture.SmDatabaseConnectionString;
        var store = _fixture.SmStoreBaseUrl;
        var site = await SqlTestData.GetOrCreateSiteForHostAsync(db, store.Host);
        var category = await SqlTestData.CreateCategoryMappingAsync(db, site.Id);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var sku = $"E2Elast-{unique}";
        await SqlTestData.InsertProductAsync(db, sku, $"Last shirt {unique}", category.FeedValue, 40m, 1);

        // Borrowed for this journey's length, as the two-country journey borrows the other store.
        var previous = await SqlTestData.SetOrderModeAsync(db, site.Id, "DirectCheckout");

        try
        {
            // The fixture's site cache is a second long.
            await Task.Delay(1_500);

            await using var firstContext = await _fixture.NewContextAsync();
            await using var secondContext = await _fixture.NewContextAsync();
            var first = await firstContext.NewPageAsync();
            var second = await secondContext.NewPageAsync();

            // Both have it in their cart while there is one left.
            await AddToCartAsync(first, store, sku);
            await AddToCartAsync(second, store, sku);

            // --- The first guest buys it ----------------------------------------------------
            var firstEmail = $"first-{unique}@example.test";
            await CheckOutAsync(first, store, firstEmail);

            await Expect(first).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/orders/[A-Za-z0-9_-]{43}\?placed=1$"),
                new() { Timeout = 20_000 });
            await Expect(first.Locator(".page-header h1")).ToContainTextAsync("SO-");
            await Expect(first.Locator(".page-header .tag")).ToHaveTextAsync("Awaiting payment");
            await Expect(first.Locator(".document__address")).ToContainTextAsync("Rua Augusta, 10");
            var reference = (await first.Locator(".page-header h1").TextContentAsync())!.Trim();

            // The token is the key, whoever holds it: a browser with no cookies opens it too.
            var orderUrl = first.Url.Split('?')[0];
            await using var strangerContext = await _fixture.NewContextAsync();
            var stranger = await strangerContext.NewPageAsync();
            await stranger.GotoAsync(orderUrl);
            await Expect(stranger.Locator(".page-header h1")).ToHaveTextAsync(reference);
            await stranger.GotoAsync(orderUrl[..^1] + (orderUrl[^1] == 'A' ? 'B' : 'A'));
            await Expect(stranger.Locator(".page-header h1")).ToHaveTextAsync("Not found");

            var placed = await ReadOrderAsync(reference);
            placed.Should().Be(new OrderRow("Consumer", "BR", 0, 1));

            // --- The second is told, and nothing is written ---------------------------------
            await CheckOutAsync(second, store, $"second-{unique}@example.test");

            await Expect(second.Locator(".document__notice")).ToContainTextAsync(sku, new() { Timeout = 20_000 });
            (await CountOrdersForAsync($"second-{unique}@example.test")).Should().Be(0);
            (await ReadOrderAsync(reference)).Stock.Should().Be(0, "the shirt was sold once");
        }
        finally
        {
            await SqlTestData.SetOrderModeAsync(db, site.Id, previous);
        }
    }

    private static async Task AddToCartAsync(IPage page, Uri store, string sku)
    {
        await page.GotoAsync(new Uri(store, $"/product/{sku}").ToString());
        await Expect(page.Locator("form.product__add button[type=submit]")).ToHaveTextAsync("Add to cart");
        await page.Locator("form.product__add button[type=submit]").ClickAsync();
    }

    private static async Task CheckOutAsync(IPage page, Uri store, string email)
    {
        await page.GotoAsync(new Uri(store, "/cart").ToString());
        await page.Locator("a.basket__checkout").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Uri(store, "/checkout").ToString());

        await page.Locator("#checkout-email").FillAsync(email);
        await page.Locator("#checkout-firstname").FillAsync("Ana");
        await page.Locator("#checkout-lastname").FillAsync("Silva");
        await page.Locator("#checkout-line1").FillAsync("Rua Augusta, 10");
        await page.Locator("#checkout-city").FillAsync("São Paulo");
        await page.Locator("#checkout-country").SelectOptionAsync("BR");
        await page.Locator(".checkout__place").ClickAsync();
    }

    private sealed record OrderRow(string Kind, string Country, int Stock, int Mailed);

    private async Task<OrderRow> ReadOrderAsync(string reference)
    {
        await using var connection = new SqlConnection(_fixture.SmDatabaseConnectionString);
        await connection.OpenAsync();

        // The mail is the storefront's to queue and StockApi's to send, so it is counted queued
        // or sent.
        await using var command = new SqlCommand(
            """
            SELECT a.[Kind], pa.[Country], pr.[QuantityInStock],
                   (SELECT COUNT(*) FROM dbo.EmailOutbox o
                    WHERE o.[TemplateKey] = N'order.placed' AND o.[ToAddress] = c.[Email])
            FROM dbo.Purchase p
            INNER JOIN dbo.Account a ON a.[Id] = p.[AccountId]
            INNER JOIN dbo.Contact c ON c.[Id] = p.[PlacedByContactId]
            INNER JOIN dbo.PurchaseAddress pa ON pa.[PurchaseId] = p.[Id]
            INNER JOIN dbo.PurchaseDetail d ON d.[PurchaseId] = p.[Id]
            INNER JOIN dbo.Product pr ON pr.[Id] = d.[ProductId]
            WHERE p.[Reference] = @Reference;
            """, connection);

        command.Parameters.AddWithValue("@Reference", reference);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        return new OrderRow(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    private async Task<int> CountOrdersForAsync(string email)
    {
        await using var connection = new SqlConnection(_fixture.SmDatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT COUNT(*) FROM dbo.Purchase p
            INNER JOIN dbo.Contact c ON c.[Id] = p.[PlacedByContactId]
            WHERE c.[Email] = @Email;
            """, connection);

        command.Parameters.AddWithValue("@Email", email);

        return (int)(await command.ExecuteScalarAsync())!;
    }
}
