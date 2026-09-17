using FluentAssertions;
using Microsoft.Data.SqlClient;
using StockManager.E2ETests.Infrastructure;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace StockManager.E2ETests.Journeys;

/// <summary>
/// Journey 7: a customer fills a basket while anonymous, signs in, and submits it.
/// </summary>
/// <remarks>
/// This is the first half of T5's exit criterion — cart, submit, and the request landing in the
/// admin queue — and it spans three things that only line up in a real browser: the basket
/// built under a cookie, the merge that hands it to a contact at sign-in, and one transaction
/// that writes a quote, its lines, and deletes the basket.
///
/// The sign-in step is the point. Anonymous submits are refused because <c>Quote.AccountId</c>
/// is NOT NULL, so the endpoint sends the customer to sign in — and their basket has to be
/// waiting for them afterwards, which is the whole reason <c>spBasket_Claim</c> exists. Asserted
/// here rather than unit-tested because the merge happens across two requests and a cookie.
/// </remarks>
[Collection(E2ECollection.Name)]
public sealed class QuoteRequestJourneyTests
{
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
