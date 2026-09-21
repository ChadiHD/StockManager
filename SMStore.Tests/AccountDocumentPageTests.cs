using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Pricing;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Navigation;
using SMStore.Ordering;
using SMStore.Sites;
using Xunit;
using AccountOrderDetail = SMStore.Components.Pages.AccountOrderDetail;
using AccountOrderPrint = SMStore.Components.Pages.AccountOrderPrint;
using AccountQuoteDetail = SMStore.Components.Pages.AccountQuoteDetail;
using AccountQuotePrint = SMStore.Components.Pages.AccountQuotePrint;
using AccountQuotes = SMStore.Components.Pages.AccountQuotes;

namespace SMStore.Tests;

/// <summary>
/// What a customer sees of their own quotes and orders, and what they must not.
/// </summary>
/// <remarks>
/// The account-scoping itself is a database property and lives in
/// <c>CustomerDocumentScopeTests</c>. What is worth asserting here is the surface on top of it:
/// that another account's reference renders "not found" rather than an error or an empty
/// document, that the accept and reject controls appear only when the quote is actually
/// decidable, and that the message after a decision comes from an allow-list rather than from
/// the query string.
/// </remarks>
public class AccountDocumentPageTests : Bunit.TestContext
{
    private const int SiteId = 7;
    private const int AccountId = 5;

    private sealed class StubAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() =>
            new("stub-token-value", "__RequestVerificationToken");
    }

    private readonly IQuoteData _quotes = Substitute.For<IQuoteData>();
    private readonly IOrderData _orders = Substitute.For<IOrderData>();
    private readonly ICatalogData _catalog = Substitute.For<ICatalogData>();
    private readonly IAccountData _accounts = Substitute.For<IAccountData>();
    private readonly ICustomerContext _customer = Substitute.For<ICustomerContext>();

    public AccountDocumentPageTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider>(new StubAntiforgeryStateProvider());

        // NSubstitute returns null for an unconfigured List<T>, and the real data access
        // never does - LoadData always returns a list. Defaulted here so a test that is
        // about one read does not have to stub the other three.
        _quotes.GetQuotesForAccount(Arg.Any<int>(), Arg.Any<int>()).Returns([]);
        _quotes.GetQuoteLinesForAccount(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns([]);
        _orders.GetOrdersForAccount(Arg.Any<int>(), Arg.Any<int>()).Returns([]);
        _orders.GetOrderLinesForAccount(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns([]);

        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(new SiteModel
        {
            Id = SiteId,
            SiteKey = "test",
            Name = "Test store",
            CurrencyCode = "EUR",
            Locale = "en-IE",
            PriceDisplay = "Public",
            OrderMode = RfqOrderingMode.ModeKey
        });
        siteContext.IsResolved.Returns(true);

        // Signed in for every test here. The anonymous case redirects, which is
        // NavigationManager behaviour rather than anything these pages decide.
        _customer.IsSignedIn.Returns(true);
        _customer.AccountId.Returns(AccountId);
        _customer.Contact.Returns(new ContactModel
        {
            Id = 41, AccountId = AccountId, FirstName = "Ada", LastName = "Byron",
            Email = "ada@example.test", Status = "Active", AccountStatus = "Approved"
        });

        var ordering = new OrderingModeProvider(siteContext, [new RfqOrderingMode()]);
        var catalogPresenter = new CatalogPresenter(
            _catalog, new PriceResolver(), siteContext, _customer);

        Services.AddSingleton(siteContext);
        Services.AddSingleton(_customer);
        Services.AddSingleton(ordering);
        Services.AddSingleton(new StoreNavigation(siteContext, ordering));
        _accounts.GetAccountById(AccountId, SiteId).Returns(new AccountModel
        {
            Id = AccountId, Company = "Byron Instruments", Reference = "AC-0005"
        });

        Services.AddSingleton(new CustomerOrderPresenter(
            _quotes, _orders, _accounts, catalogPresenter, siteContext, _customer, ordering,
            NullLogger<CustomerOrderPresenter>.Instance));
    }

    private void QuoteIs(string reference, string status, DateTime? expires = null, string? note = null)
    {
        _quotes.GetQuoteForAccount(reference, AccountId, SiteId).Returns(new QuoteModel
        {
            Id = 3,
            Reference = reference,
            AccountId = AccountId,
            Currency = "EUR",
            Status = status,
            CreatedDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresDate = expires,
            CustomerNote = note,
            Lines = 1,
            Value = 200m
        });

        _quotes.GetQuoteLinesForAccount(3, AccountId, SiteId).Returns([
            new QuoteLineModel
            {
                Id = 1, QuoteId = 3, ProductId = 12, Sku = "SKU1", Name = "Widget",
                Quantity = 2, ListPrice = 100m, DiscountPct = 0m, NetPrice = 100m
            }
        ]);
    }

    // --- The list ---

    [Fact]
    public void AnEmptyQuoteListPointsAtTheBasketRatherThanNowhere()
    {
        var cut = RenderComponent<AccountQuotes>();

        // The route comes from IOrderingMode, so a checkout store sends them to /cart.
        cut.Find(".stub a").GetAttribute("href").Should().Be("/quote");
    }

    [Fact]
    public void TheQuoteListLinksEachRowToItsOwnPage()
    {
        _quotes.GetQuotesForAccount(AccountId, SiteId).Returns([
            new QuoteModel
            {
                Id = 3, Reference = "QT-0041", AccountId = AccountId, Status = "Priced",
                CreatedDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Lines = 2, Value = 350m
            }
        ]);

        var cut = RenderComponent<AccountQuotes>();

        cut.Find("table.document-list a").GetAttribute("href")
            .Should().Be("/account/quotes/QT-0041");
    }

    // --- The quote ---

    [Fact]
    public void AnotherAccountsReferenceRendersNotFoundRatherThanAnEmptyDocument()
    {
        // The presenter returns null for a reference that is not this account's, and for one
        // that names nothing at all. The page must not tell them apart either.
        var cut = RenderComponent<AccountQuoteDetail>(p => p.Add(x => x.Reference, "QT-9999"));

        cut.Markup.Should().Contain("Not found");
        cut.FindAll("form.document__accept").Should().BeEmpty();
        cut.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void APricedQuoteOffersAcceptAndRejectWithAntiforgeryOnBoth()
    {
        QuoteIs("QT-0041", "Priced", DateTime.UtcNow.AddDays(7));

        var cut = RenderComponent<AccountQuoteDetail>(p => p.Add(x => x.Reference, "QT-0041"));

        var accept = cut.Find("form.document__accept");
        var reject = cut.Find("form.document__reject");

        accept.GetAttribute("action").Should().Be(QuoteDecisionEndpoints.AcceptPath);
        reject.GetAttribute("action").Should().Be(QuoteDecisionEndpoints.RejectPath);

        // Accepting a quote places an order, so it must never be reachable by anything that
        // makes a browser fetch a URL.
        accept.GetAttribute("method").Should().Be("post");
        accept.QuerySelector("input[type=hidden][name='__RequestVerificationToken']")
            .Should().NotBeNull();
        reject.QuerySelector("input[type=hidden][name='__RequestVerificationToken']")
            .Should().NotBeNull();

        // The PO number is optional and the reason is not, which is why one has a required
        // prompt and the other does not.
        accept.QuerySelector("input[name='poNumber']").Should().NotBeNull();
        reject.QuerySelector("textarea[name='reason']").Should().NotBeNull();
    }

    [Theory]
    [InlineData("Requested", "We are pricing this now")]
    [InlineData("Accepted", "it is now an order")]
    [InlineData("Rejected", "was turned down")]
    public void AQuoteThatIsNotAwaitingADecisionSaysWhyInsteadOfOfferingButtons(
        string status, string expected)
    {
        QuoteIs("QT-0041", status);

        var cut = RenderComponent<AccountQuoteDetail>(p => p.Add(x => x.Reference, "QT-0041"));

        cut.FindAll("form.document__accept").Should().BeEmpty();
        cut.Markup.Should().Contain(expected);
    }

    [Fact]
    public void AnExpiredQuoteOffersARequoteRatherThanAnAcceptButton()
    {
        QuoteIs("QT-0041", "Priced", DateTime.UtcNow.AddDays(-1));

        var cut = RenderComponent<AccountQuoteDetail>(p => p.Add(x => x.Reference, "QT-0041"));

        cut.FindAll("form.document__accept").Should().BeEmpty();
        cut.Markup.Should().Contain("expired");
    }

    [Fact]
    public void TheCustomersOwnNoteIsRenderedAsTextAndNotAsMarkup()
    {
        QuoteIs("QT-0041", "Priced", DateTime.UtcNow.AddDays(7),
            note: "<script>alert(1)</script> before month end");

        var cut = RenderComponent<AccountQuoteDetail>(p => p.Add(x => x.Reference, "QT-0041"));

        // CustomerNote is the one column on dbo.Quote whose contents originate with a
        // customer, so it must never reach a MarkupString — the SiteContent.BodyHtml rule in
        // reverse. Blazor escapes by default; this is the assertion that keeps it that way.
        cut.Markup.Should().NotContain("<script>");
        cut.Markup.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void TheMessageAfterADecisionComesFromAnAllowListAndNotFromTheQueryString()
    {
        QuoteIs("QT-0041", "Priced", DateTime.UtcNow.AddDays(7));

        // Through the URL, not as a parameter: bUnit refuses a SupplyParameterFromQuery
        // value passed directly, which is the same thing PasswordResetPageTests ran into.
        var injected = RenderWithDecision("<b>anything I like</b>");

        // The parameter arrives in a URL anyone can write, inside a session. Echoing it
        // would put attacker-chosen text on the page beside the customer's own prices.
        injected.FindAll(".document__notice").Should().BeEmpty();
        injected.Markup.Should().NotContain("anything I like");

        RenderWithDecision("AlreadyDecided").Find(".document__notice")
            .TextContent.Should().Contain("Somebody at your company");
    }

    private IRenderedComponent<AccountQuoteDetail> RenderWithDecision(string decision)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo(navigation.GetUriWithQueryParameter("decision", decision));

        return RenderComponent<AccountQuoteDetail>(p => p.Add(x => x.Reference, "QT-0041"));
    }

    // --- The order ---

    [Fact]
    public void AnOrderShowsTheCustomersOwnPurchaseOrderNumberAndItsQuote()
    {
        _orders.GetOrderForAccount("SO-0012", AccountId, SiteId).Returns(new OrderModel
        {
            Id = 9,
            Reference = "SO-0012",
            AccountId = AccountId,
            Currency = "EUR",
            Status = "Awaiting payment",
            PurchaseDate = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            SubTotal = 200m,
            FinalPrice = 200m,
            PoNumber = "PO-99123",
            FromQuoteReference = "QT-0041",
            Items = 1
        });

        _orders.GetOrderLinesForAccount(9, AccountId, SiteId).Returns([
            new OrderLineModel
            {
                Id = 1, PurchaseId = 9, ProductId = 12, Sku = "SKU1", Name = "Widget",
                Quantity = 2, Price = 100m
            }
        ]);

        var cut = RenderComponent<AccountOrderDetail>(p => p.Add(x => x.Reference, "SO-0012"));

        // Their reference, not ours: many B2B buyers cannot pay an invoice that does not carry
        // it, so the order has to show it back to them.
        cut.Markup.Should().Contain("PO-99123");
        // And the quote it came from, so the trail is walkable in both directions.
        cut.Find("a.mono").GetAttribute("href").Should().Be("/account/quotes/QT-0041");
    }

    [Fact]
    public void AnotherAccountsOrderReferenceRendersNotFound()
    {
        var cut = RenderComponent<AccountOrderDetail>(p => p.Add(x => x.Reference, "SO-9999"));

        cut.Markup.Should().Contain("Not found");
        cut.FindAll("table").Should().BeEmpty();
    }

    // --- The printable document ---
    //
    // No PDF renderer sits behind these pages: T5 needed the document, and T6 decides whether
    // an attachable file is needed once there is an outbox to attach it to. What is worth
    // asserting is therefore the content, which is the same under every candidate renderer.

    [Fact]
    public void APrintedQuoteNamesTheStoreThatIssuedItAndTheCompanyItIsFor()
    {
        QuoteIs("QT-0041", "Priced", DateTime.UtcNow.AddDays(7));

        var cut = RenderComponent<AccountQuotePrint>(p => p.Add(x => x.Reference, "QT-0041"));

        // Both halves are per-tenant: the store comes from ISiteContext and the company from
        // the account the session resolves to. A document carrying neither is a page of
        // numbers nobody can file.
        cut.Markup.Should().Contain("Test store");
        cut.Markup.Should().Contain("Byron Instruments");
        cut.Find(".sheet__reference").TextContent.Should().Be("QT-0041");
    }

    [Fact]
    public void APrintedQuoteEscapesTheCustomersOwnWords()
    {
        QuoteIs("QT-0041", "Priced", DateTime.UtcNow.AddDays(7),
            note: "<script>alert('x')</script> needed before month end");

        var cut = RenderComponent<AccountQuotePrint>(p => p.Add(x => x.Reference, "QT-0041"));

        // CustomerNote is one of the two columns whose contents originate with a customer, so
        // the SiteContent.BodyHtml rule runs in reverse. Asserted here as well as on the
        // detail page because this is the copy somebody forwards.
        cut.Markup.Should().NotContain("<script>");
        cut.Markup.Should().Contain("needed before month end");
    }

    [Fact]
    public void APrintedQuoteCarriesTheWordTheStoreUsesForIt()
    {
        QuoteIs("QT-0041", "Priced", DateTime.UtcNow.AddDays(7));

        var cut = RenderComponent<AccountQuotePrint>(p => p.Add(x => x.Reference, "QT-0041"));

        // From IOrderingMode, like every other customer-facing word. A checkout store's
        // document says Cart rather than Quote without this page changing.
        cut.Find(".sheet__kind").TextContent.Should().Be(new RfqOrderingMode().BasketLabel);
    }

    [Fact]
    public void APrintedOrderShowsThePurchaseOrderNumberAndTheQuoteItCameFrom()
    {
        OrderIs("SO-0012", poNumber: "PO-99123", fromQuote: "QT-0041");

        var cut = RenderComponent<AccountOrderPrint>(p => p.Add(x => x.Reference, "SO-0012"));

        cut.Markup.Should().Contain("PO-99123");
        cut.Markup.Should().Contain("QT-0041");
    }

    [Fact]
    public void APrintedOrderLeavesOutTheRowsItHasNothingToPutIn()
    {
        OrderIs("SO-0012", poNumber: null, fromQuote: null);

        var cut = RenderComponent<AccountOrderPrint>(p => p.Add(x => x.Reference, "SO-0012"));

        // An order placed by an admin has no PO number and one raised directly has no quote.
        // A labelled empty row on a document somebody files reads as missing data.
        cut.Markup.Should().NotContain("Your PO number");
        cut.Markup.Should().NotContain("From quote");
        cut.Find(".sheet__reference").TextContent.Should().Be("SO-0012");
    }

    [Fact]
    public void AnotherAccountsReferenceCannotBePrintedEither()
    {
        var cut = RenderComponent<AccountQuotePrint>(p => p.Add(x => x.Reference, "QT-9999"));

        // The print route is a second way in to the same document, so it answers the same way:
        // "not yours" and "not here" are one answer.
        cut.Markup.Should().Contain("Not found");
        cut.FindAll(".sheet").Should().BeEmpty();
    }

    private void OrderIs(string reference, string? poNumber, string? fromQuote)
    {
        _orders.GetOrderForAccount(reference, AccountId, SiteId).Returns(new OrderModel
        {
            Id = 9,
            Reference = reference,
            AccountId = AccountId,
            Currency = "EUR",
            Status = "Awaiting payment",
            PurchaseDate = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            SubTotal = 200m,
            FinalPrice = 200m,
            PoNumber = poNumber,
            FromQuoteReference = fromQuote,
            Items = 1
        });

        _orders.GetOrderLinesForAccount(9, AccountId, SiteId).Returns([
            new OrderLineModel
            {
                Id = 1, PurchaseId = 9, ProductId = 12, Sku = "SKU1", Name = "Widget",
                Quantity = 2, Price = 100m
            }
        ]);
    }
}
