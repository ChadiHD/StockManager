using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Pricing;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Ordering;
using SMStore.Sites;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// Which basket belongs to a request, and what the cookie carrying it is allowed to be.
/// </summary>
/// <remarks>
/// <c>BasketService</c> exists so no page or endpoint has to reason about tokens, cookies and
/// the signed-in contact together, which makes it the one place those rules are worth pinning
/// down. Most of what is asserted here is restraint: reading creates nothing, a malformed
/// cookie is not looked up, an unknown SKU is refused, and a basket handover that fails does
/// not fail the sign-in that triggered it.
/// </remarks>
public class BasketServiceTests
{
    private const string SiteKey = "test";
    private const int SiteId = 7;
    private const string Sku = "SKU1";
    private const int ProductId = 12;

    private readonly IBasketData _baskets = Substitute.For<IBasketData>();
    private readonly ICatalogData _catalog = Substitute.For<ICatalogData>();
    private readonly ICustomerContext _customer = Substitute.For<ICustomerContext>();
    private readonly DefaultHttpContext _http = new();

    private BasketService ServiceFor(ContactModel? contact = null)
    {
        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(new SiteModel
        {
            Id = SiteId,
            SiteKey = SiteKey,
            Name = "Test store",
            Domain = "test.example",
            CurrencyCode = "EUR",
            PriceDisplay = "Public",
            OrderMode = "Rfq"
        });
        siteContext.IsResolved.Returns(true);

        _customer.Contact.Returns(contact);
        _customer.CustomerGroupId.Returns(contact?.CustomerGroupId);

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(_http);

        // The real PriceResolver rather than a substitute: it is pure, and the point of routing
        // the basket through CatalogPresenter is that it prices the way the catalog does.
        var presenter = new CatalogPresenter(_catalog, new PriceResolver(), siteContext, _customer);

        return new BasketService(
            _baskets, presenter, siteContext, _customer, accessor,
            NullLogger<BasketService>.Instance);
    }

    private static ContactModel Contact(int id = 41, int? groupId = 3) => new()
    {
        Id = id,
        AccountId = 5,
        FirstName = "Ada",
        LastName = "Byron",
        Email = "ada@example.test",
        CustomerGroupId = groupId,
        Status = "Active",
        AccountStatus = "Approved"
    };

    /// <summary>Makes the SKU resolvable for this viewer, as spCatalog_GetBySku would.</summary>
    private void CatalogHas(string sku = Sku, int? groupId = null) =>
        _catalog.GetBySku(SiteId, sku, groupId).Returns(new CatalogItemModel
        {
            Id = ProductId,
            Sku = sku,
            ProductName = "Widget",
            RetailPrice = 100m,
            Cost = 60m,
            QuantityInStock = 5
        });

    private void WithCookie(string value) =>
        _http.Request.Headers.Cookie = $"{BasketToken.CookieName}={value}";

    private string? SetCookieValue() =>
        _http.Response.Headers.SetCookie
            .FirstOrDefault(header => header?.StartsWith(BasketToken.CookieName) == true);

    // --- Reading ---

    [Fact]
    public void ReadingWithNoCookieAndNoSessionCostsNoQueryAtAll()
    {
        ServiceFor().Lines().Should().BeEmpty();

        // The header counts the basket on every page, and a first-time visitor has neither a
        // cookie nor a session, so neither lookup could match. A basket row per page view would
        // also be a row per crawler, which is why only an add reaches EnsureBasket.
        _baskets.DidNotReceive().FindBasket(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>());
        _baskets.DidNotReceive().EnsureBasket(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>());
    }

    [Fact]
    public void AMalformedCookieIsTreatedAsNoCookieAtAll()
    {
        WithCookie("not-a-token");

        ServiceFor().Lines();

        // Shape-checked before it reaches a query: a caller cannot spray arbitrary cookie
        // values and have each one looked up. With no session either, there is nothing to ask.
        _baskets.DidNotReceive().FindBasket(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>());
    }

    [Fact]
    public void AWellFormedCookieIsUsedToFindTheBasket()
    {
        var token = BasketToken.Mint();
        WithCookie(token);

        ServiceFor().Lines();

        _baskets.Received(1).FindBasket(SiteId, token, null);
    }

    [Fact]
    public void ASignedInCustomerIsLookedUpByContactAndTheirGroupReachesTheQuery()
    {
        var token = BasketToken.Mint();
        WithCookie(token);

        _baskets.FindBasket(SiteId, token, 41)
            .Returns(new BasketModel { Id = 9, SiteId = SiteId, ContactId = 41 });

        ServiceFor(Contact()).Lines();

        // The group decides what is still visible to them, so it has to arrive with the read.
        // From the session, never from a form field — the same rule the catalog follows.
        _baskets.Received(1).GetLines(9, SiteId, 3);
    }

    [Fact]
    public void TheLineCountCountsProductsRatherThanUnits()
    {
        var token = BasketToken.Mint();
        WithCookie(token);

        _baskets.FindBasket(SiteId, token, null).Returns(new BasketModel { Id = 9, SiteId = SiteId });
        _baskets.GetLines(9, SiteId, null).Returns([
            new BasketLineModel { ProductId = 1, Quantity = 4 },
            new BasketLineModel { ProductId = 2, Quantity = 1 }
        ]);

        // "5" beside a basket icon holding two products reads as five products.
        ServiceFor().LineCount().Should().Be(2);
    }

    // --- Adding ---

    [Fact]
    public void TheFirstAddMintsACookieThatScriptsAndOtherSitesCannotUse()
    {
        CatalogHas();
        _baskets.EnsureBasket(SiteId, Arg.Any<string>(), null)
            .Returns(new BasketModel { Id = 9, SiteId = SiteId });

        ServiceFor().Add(Sku, quantity: 2).Should().BeTrue();

        var cookie = SetCookieValue();

        cookie.Should().NotBeNull();
        // The token is the authorisation to read the basket, so a script that could read it
        // could take the basket.
        cookie.Should().Contain("httponly", Exactly.Once());
        cookie.Should().Contain("secure", Exactly.Once());
        // Lax rather than Strict: a customer arriving from an emailed link or a search result
        // is on a cross-site navigation, and Strict would withhold the cookie on exactly that
        // request — so the basket would look empty on the page they land on.
        cookie.Should().Contain("samesite=lax", Exactly.Once());
    }

    [Fact]
    public void AnAddResolvesTheSkuForThisViewerAndPassesTheProductItFound()
    {
        CatalogHas();
        _baskets.EnsureBasket(SiteId, Arg.Any<string>(), null)
            .Returns(new BasketModel { Id = 9, SiteId = SiteId });

        ServiceFor().Add($"  {Sku}  ", quantity: 3);

        // The SKU comes out of a form, so resolving it through the catalog is the first of two
        // visibility checks — spBasket_AddLine asks the same question again.
        _baskets.Received(1).AddLine(9, SiteId, ProductId, 3, null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT-IN-THIS-STORE")]
    public void ASkuThisViewerCannotSeeIsRefusedWithoutTouchingTheBasket(string? sku)
    {
        CatalogHas();

        // Another store's product, one hidden from this group, one that went stale or delisted
        // since the page loaded, and an empty box: one answer, because the customer can do the
        // same thing about each.
        ServiceFor().Add(sku, quantity: 1).Should().BeFalse();

        _baskets.DidNotReceive().EnsureBasket(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>());
    }

    [Fact]
    public void AnAddThatTheDatabaseRefusesIsReportedAndNotThrown()
    {
        CatalogHas();
        _baskets.EnsureBasket(SiteId, Arg.Any<string>(), null)
            .Returns(new BasketModel { Id = 9, SiteId = SiteId });
        _baskets.When(baskets => baskets.AddLine(9, SiteId, ProductId, 1, null))
            .Throw(new InvalidOperationException("not available in this store"));

        // The expected case is a product that went stale or was delisted between the page
        // rendering and the button being pressed, which is a message rather than an error page.
        ServiceFor().Add(Sku, quantity: 1).Should().BeFalse();
    }

    [Fact]
    public void AnExistingCookieIsReusedRatherThanReplaced()
    {
        var token = BasketToken.Mint();
        WithCookie(token);
        CatalogHas();

        _baskets.EnsureBasket(SiteId, token, null)
            .Returns(new BasketModel { Id = 9, SiteId = SiteId, Token = token });

        ServiceFor().Add(Sku, quantity: 1);

        _baskets.Received(1).EnsureBasket(SiteId, token, null);
        SetCookieValue().Should().BeNull("a second Set-Cookie would reset the expiry pointlessly");
    }

    // --- Changing ---

    [Fact]
    public void ChangingALineWithNoBasketDoesNothingRatherThanCreatingOne()
    {
        ServiceFor().SetQuantity(Sku, quantity: 3).Should().BeFalse();

        _baskets.DidNotReceive().SetQuantity(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void AQuantityChangeResolvesTheSkuAgainstTheBasketAndNotTheCatalog()
    {
        var token = BasketToken.Mint();
        WithCookie(token);

        _baskets.FindBasket(SiteId, token, null).Returns(new BasketModel { Id = 9, SiteId = SiteId });
        _baskets.GetLines(9, SiteId, null).Returns([
            new BasketLineModel { ProductId = ProductId, Sku = Sku, Quantity = 2, Available = false }
        ]);
        _baskets.SetQuantity(9, SiteId, ProductId, 0).Returns(true);

        // Available is false here on purpose: a line whose product has since been delisted must
        // still be removable, and spCatalog_GetBySku would no longer return it.
        ServiceFor().SetQuantity(Sku, quantity: 0).Should().BeTrue();

        _baskets.Received(1).SetQuantity(9, SiteId, ProductId, 0);
        _catalog.DidNotReceive().GetBySku(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>());
    }

    [Fact]
    public void AQuantityChangeForASkuNotInTheBasketChangesNothing()
    {
        var token = BasketToken.Mint();
        WithCookie(token);

        _baskets.FindBasket(SiteId, token, null).Returns(new BasketModel { Id = 9, SiteId = SiteId });
        _baskets.GetLines(9, SiteId, null).Returns([
            new BasketLineModel { ProductId = ProductId, Sku = Sku, Quantity = 2, Available = true }
        ]);

        ServiceFor().SetQuantity("SOMEONE-ELSES-SKU", quantity: 5).Should().BeFalse();

        _baskets.DidNotReceive().SetQuantity(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    // --- Sign-in and sign-out ---

    [Fact]
    public void SignInHandsTheBrowsersBasketToTheContact()
    {
        var token = BasketToken.Mint();
        WithCookie(token);

        ServiceFor().ClaimFor(contactId: 41);

        _baskets.Received(1).ClaimBasket(SiteId, token, 41);
    }

    [Fact]
    public void SignInWithNoBasketCookieClaimsNothing()
    {
        ServiceFor().ClaimFor(contactId: 41);

        _baskets.DidNotReceive().ClaimBasket(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public void AFailedHandoverNeverEscapesIntoTheSignIn()
    {
        WithCookie(BasketToken.Mint());

        _baskets.ClaimBasket(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(_ => throw new InvalidOperationException("database asleep"));

        // Somebody who has just proved who they are gets in whether or not their basket came
        // with them. Thrown, this would land them back on the login page with the same
        // indistinguishable failure a wrong password produces.
        ServiceFor().ClaimFor(contactId: 41);
    }

    [Fact]
    public void SignOutDropsTheBasketCookie()
    {
        WithCookie(BasketToken.Mint());

        ServiceFor().Forget();

        // A claimed basket keeps its token, so the cookie left behind names a basket that now
        // belongs to a customer. spBasket_Find refuses to serve one to an anonymous caller as
        // well; neither half is sufficient alone.
        SetCookieValue().Should().Contain("expires=Thu, 01 Jan 1970");
    }
}

/// <summary>
/// The shape of a basket token, which is the only thing standing between an anonymous visitor
/// and somebody else's basket.
/// </summary>
public class BasketTokenTests
{
    [Fact]
    public void AMintedTokenIsAcceptedAndUnpredictable()
    {
        var first = BasketToken.Mint();
        var second = BasketToken.Mint();

        BasketToken.IsWellFormed(first).Should().BeTrue();
        first.Should().NotBe(second);
        // 256 bits of RNG output, base64url, no padding. A row id or a GUID would be guessable
        // or partly structured, and this value is a bearer credential.
        first.Should().HaveLength(43);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]   // 44: one too many
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]     // 42: one too few
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]    // base64, not base64url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("'; DROP TABLE dbo.Basket; --                ")]
    public void AnythingElseIsNotATokenThisApplicationMinted(string? value) =>
        BasketToken.IsWellFormed(value).Should().BeFalse();
}
