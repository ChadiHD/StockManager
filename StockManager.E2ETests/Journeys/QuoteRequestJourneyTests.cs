using FluentAssertions;
using Microsoft.Data.SqlClient;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 7: a customer fills a basket while anonymous, signs in, submits, and accepts.
/// </summary>
/// <remarks>
/// T5's exit criterion in one pass, with nothing done in SQL: cart, submit, price in the admin
/// portal, accept, order exists.
///
/// It spans six things that only line up in a real browser: the basket built under a cookie,
/// the merge that hands it to a contact at sign-in, one transaction that writes a quote and
/// deletes the basket, an admin re-pricing a line in a WebAssembly portal against the API, the
/// claim that makes the quote decidable, and the acceptance that turns it into an order
/// recording the contact rather than a staff member. Two browsers, two hosts and two databases.
///
/// The re-price is not decoration. It is what proves the price the customer accepts is the one
/// the admin set rather than the one the storefront resolved at submit — and the quantity
/// asserted on the order at the end is the number typed into the portal, so the whole chain has
/// to hold for that assertion to pass.
///
/// The sign-in step is the point. Anonymous submits are refused because <c>Quote.AccountId</c>
/// is NOT NULL, so the endpoint sends the customer to sign in — and their basket has to be
/// waiting for them afterwards, which is the whole reason <c>spBasket_Claim</c> exists. Asserted
/// here rather than unit-tested because the merge happens across two requests and a cookie.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class QuoteRequestJourneyTests
{
    /// <summary>
    /// What the admin re-quantifies the first line to. Deliberately not 1 or 2, so it cannot
    /// coincide with what the basket put there.
    /// </summary>
    private const int RepricedQuantity = 4;

    private readonly AspireAppFixture _fixture;

    public QuoteRequestJourneyTests(AspireAppFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Customer_fills_a_basket_anonymously_signs_in_and_submits_it()
    {
        Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
        Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);

        var site = await SqlTestData.GetOrCreateSiteForHostAsync(
            _fixture.SmDatabaseConnectionString, _fixture.SmStoreBaseUrl.Host);

        var category = await SqlTestData.CreateCategoryMappingAsync(
            _fixture.SmDatabaseConnectionString, site.Id);

        var skuPrefix = $"E2Eqr{Guid.NewGuid():N}"[..10];

        await SqlTestData.InsertProductsAsync(
            _fixture.SmDatabaseConnectionString, skuPrefix, category.FeedValue, count: 2);

        var customer = await IdentityTestSupport.CreateApprovedCustomerAsync(
            _fixture.ApiAuthConnectionString, _fixture.SmDatabaseConnectionString,
            site.SiteKey, site.Id);

        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        // Anonymous, and the basket is found by a cookie.
        await page.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, $"/catalog?cat={category.Slug}").ToString());
        await page.Locator(".product-card__add").First.ClickAsync();
        await page.Locator(".product-card__add").Last.ClickAsync();
        await Expect(page.Locator(".site-header__count")).ToHaveTextAsync("2");

        await page.Locator(".site-header__basket").ClickAsync();
        await page.Locator("form.basket__submit textarea[name=note]")
            .FillAsync("Needed before month end");
        await page.Locator("form.basket__submit button[type=submit]").ClickAsync();

        // A quote belongs to an account, so an anonymous submit is a trip to sign in rather
        // than a refusal. The basket has to survive it.
        await page.WaitForURLAsync(url => url.Contains("/login"));

        await StoreFront.SignInAsync(
            page, _fixture.SmStoreBaseUrl, customer.Email, customer.Password);

        await page.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, "/quote").ToString());

        // spBasket_Claim merged the cookie's basket into the contact's, so both lines are still
        // here. Without it, identifying yourself would cost you your list.
        await Expect(page.Locator("table.basket__table tbody tr")).ToHaveCountAsync(2);

        await page.Locator("form.basket__submit textarea[name=note]")
            .FillAsync("Needed before month end");
        await page.Locator("form.basket__submit button[type=submit]").ClickAsync();

        await page.WaitForURLAsync(url => url.Contains("/quote/submitted"));

        var reference = await page.Locator(".submitted__reference").TextContentAsync();

        reference.Should().StartWith("QT-");

        // The quote, its lines and the customer's note, as the admin queue will read them.
        var quote = await ReadQuoteAsync(reference!.Trim());

        quote.Status.Should().Be("Requested");
        quote.SiteId.Should().Be(site.Id);
        quote.Lines.Should().Be(2);
        quote.CustomerNote.Should().Be("Needed before month end");

        // And the basket is gone, in the same transaction. Left behind, it would be sitting
        // there inviting the customer to send the same request again.
        await page.GotoAsync(new Uri(_fixture.SmStoreBaseUrl, "/quote").ToString());
        await Expect(page.Locator("table.basket__table")).ToHaveCountAsync(0);
        await Expect(page.Locator(".site-header__count")).ToHaveCountAsync(0);

        // A Requested quote is not decidable: a customer accepting a price nobody has set is
        // not the same act as an admin converting one for somebody who rang up.
        var quotePage = new Uri(_fixture.SmStoreBaseUrl, $"/account/quotes/{reference.Trim()}").ToString();

        await page.GotoAsync(quotePage);
        await Expect(page.Locator("form.document__accept")).ToHaveCountAsync(0);
        await Expect(page.Locator(".document__summary")).ToContainTextAsync("pricing this now");

        // --- An admin prices it -------------------------------------------------------
        await using var adminContext = await _fixture.NewContextAsync();
        var adminPage = await adminContext.NewPageAsync();

        await AdminPortal.SignInAsync(
            adminPage, _fixture.SmPortalBaseUrl, _fixture.Admin.Email, _fixture.Admin.Password);

        await AdminPortal.EnsureActingForSiteAsync(adminPage, site.SiteKey);

        var adminQuote = new Uri(
            _fixture.SmPortalBaseUrl, $"/admin/quotes/{reference.Trim()}").ToString();

        await adminPage.GotoAsync(adminQuote);

        // Re-quantify the first line. Save draft wrote nothing at all before T5 item 7, so
        // this is the write as well as the setup for what the customer accepts.
        var quantity = adminPage.Locator("input.line-edit").First;

        await quantity.FillAsync(RepricedQuantity.ToString());

        // Tab rather than trusting fill() alone: Blazor binds @onchange to the DOM change
        // event, and the blur is what a person typing into the box actually produces.
        await quantity.PressAsync("Tab");

        await Expect(adminPage.Locator("button.quote-save")).ToBeEnabledAsync();
        await adminPage.Locator("button.quote-save").ClickAsync();
        await Expect(adminPage.Locator(".toast")).ToContainTextAsync("re-priced");

        // Reloaded rather than trusting the toast: the assertion is that the edit survived the
        // round trip, and it also clears the toast so the next one cannot match the last.
        await adminPage.GotoAsync(adminQuote);

        // 20 seconds, like the portal sign-in, and for the same reason: a reload is a cold
        // WebAssembly boot plus AdminLayout awaiting its snapshot, which is comfortably inside
        // five seconds on its own and is not when the whole suite is running.
        await Expect(adminPage.Locator("input.line-edit").First)
            .ToHaveValueAsync(RepricedQuantity.ToString(), new() { Timeout = 20_000 });

        await adminPage.Locator("button.quote-send").ClickAsync();
        await Expect(adminPage.Locator(".toast")).ToContainTextAsync("they can accept it now");

        await page.GotoAsync(quotePage);
        await page.Locator("form.document__accept input[name=poNumber]").FillAsync("PO-E2E-1");
        await page.Locator("form.document__accept button[type=submit]").ClickAsync();

        // An accepted quote is an order, so that is where the customer lands. Sending them back
        // to a quote that now reads "Accepted" would leave them looking for what happened.
        await page.WaitForURLAsync(url => url.Contains("/account/orders/SO-"));

        await Expect(page.Locator("h1.mono")).ToContainTextAsync("SO-");
        await Expect(page.Locator(".document__summary")).ToContainTextAsync("PO-E2E-1");

        var order = await ReadOrderAsync(reference.Trim());

        order.Lines.Should().Be(2);
        order.PoNumber.Should().Be("PO-E2E-1");
        // The quantity the admin typed, on the order the customer accepted. Both basket lines
        // were added once, so anything other than this number means the re-price did not
        // reach the document — the one assertion that spans every hop in this journey.
        order.TopQuantity.Should().Be(RepricedQuantity);
        // The placer is the contact, and StaffId is NULL. dbo.[User] holds staff, so a
        // customer acceptance has nothing to put there — and CK_Purchase_Placer refuses a row
        // with both or neither, which is what makes this assertion meaningful rather than
        // incidental.
        order.PlacedByContactId.Should().NotBeNull();
        order.StaffId.Should().BeNull();
    }

    private sealed record OrderRow(
        int Lines, int TopQuantity, string? PoNumber, int? PlacedByContactId, string? StaffId);

    private async Task<OrderRow> ReadOrderAsync(string quoteReference)
    {
        await using var connection = new SqlConnection(_fixture.SmDatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT p.[PoNumber], p.[PlacedByContactId], p.[StaffId],
                   COUNT(d.[Id]) AS [Lines], MAX(d.[Quantity]) AS [TopQuantity]
            FROM dbo.Purchase p
            INNER JOIN dbo.Quote q ON q.[Id] = p.[QuoteId]
            LEFT JOIN dbo.PurchaseDetail d ON d.[PurchaseId] = p.[Id]
            WHERE q.[Reference] = @Reference
            GROUP BY p.[PoNumber], p.[PlacedByContactId], p.[StaffId];
            """, connection);

        command.Parameters.AddWithValue("@Reference", quoteReference);

        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue($"{quoteReference} should have become an order");

        return new OrderRow(
            reader.GetInt32(3),
            reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private sealed record QuoteRow(string Status, int SiteId, int Lines, string? CustomerNote);

    private async Task<QuoteRow> ReadQuoteAsync(string reference)
    {
        await using var connection = new SqlConnection(_fixture.SmDatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT q.[Status], q.[SiteId], q.[CustomerNote], COUNT(l.[Id]) AS [Lines]
            FROM dbo.Quote q
            LEFT JOIN dbo.QuoteLine l ON l.[QuoteId] = q.[Id]
            WHERE q.[Reference] = @Reference
            GROUP BY q.[Status], q.[SiteId], q.[CustomerNote];
            """, connection);

        command.Parameters.AddWithValue("@Reference", reference);

        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue($"{reference} should exist");

        return new QuoteRow(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(3),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }
}
