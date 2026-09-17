using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Accounts;
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
/// cookie is not looked up, and a basket handover that fails does not fail the sign-in that
/// triggered it.
/// </remarks>
public class BasketServiceTests
{
    private const string SiteKey = "test";
    private const int SiteId = 7;

    private readonly IBasketData _baskets = Substitute.For<IBasketData>();
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
            OrderMode = "Rfq"
        });
        siteContext.IsResolved.Returns(true);

        _customer.Contact.Returns(contact);
        _customer.CustomerGroupId.Returns(contact?.CustomerGroupId);

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(_http);

        return new BasketService(
            _baskets, siteContext, _customer, accessor, NullLogger<BasketService>.Instance);
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

    private void WithCookie(string value) =>
        _http.Request.Headers.Cookie = $"{BasketToken.CookieName}={value}";

    private string? SetCookieValue() =>
        _http.Response.Headers.SetCookie
            .FirstOrDefault(header => header?.StartsWith(BasketToken.CookieName) == true);

    // --- Reading ---

    [Fact]
    public void ReadingWithNoCookieCreatesNothing()
    {
        var service = ServiceFor();

        service.Lines().Should().BeEmpty();

        // A basket row per page view would be a row per crawler. EnsureBasket is the only
        // thing that creates one, and only an add reaches it.
        _baskets.DidNotReceive().EnsureBasket(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>());
    }

    [Fact]
    public void AMalformedCookieIsTreatedAsNoCookieAtAll()
    {
        WithCookie("not-a-token");

        var service = ServiceFor();

        service.Lines();

        // Shape-checked before it reaches a query: a caller cannot spray arbitrary cookie
        // values and have each one looked up, and a mangled cookie produces a fresh basket
        // rather than a lookup nothing can ever match.
        _baskets.Received(1).FindBasket(SiteId, null, null);
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
        _baskets.EnsureBasket(SiteId, Arg.Any<string>(), null)
            .Returns(new BasketModel { Id = 9, SiteId = SiteId });

        ServiceFor().Add(productId: 12, quantity: 2).Should().BeTrue();

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
    public void AnAddThatTheDatabaseRefusesIsReportedAndNotThrown()
    {
        _baskets.EnsureBasket(SiteId, Arg.Any<string>(), null)
            .Returns(new BasketModel { Id = 9, SiteId = SiteId });
        _baskets.When(baskets => baskets.AddLine(9, SiteId, 12, 1, null))
            .Throw(new InvalidOperationException("not available in this store"));

        // The expected case is a product that went stale or was delisted between the page
        // rendering and the button being pressed, which is a message rather than an error page.
        ServiceFor().Add(productId: 12, quantity: 1).Should().BeFalse();
    }

    [Fact]
    public void AnExistingCookieIsReusedRatherThanReplaced()
    {
        var token = BasketToken.Mint();
        WithCookie(token);

        _baskets.EnsureBasket(SiteId, token, null)
            .Returns(new BasketModel { Id = 9, SiteId = SiteId, Token = token });

        ServiceFor().Add(productId: 12, quantity: 1);

        _baskets.Received(1).EnsureBasket(SiteId, token, null);
        SetCookieValue().Should().BeNull("a second Set-Cookie would reset the expiry pointlessly");
    }

    // --- Changing ---

    [Fact]
    public void ChangingALineWithNoBasketDoesNothingRatherThanCreatingOne()
    {
        ServiceFor().SetQuantity(productId: 12, quantity: 3).Should().BeFalse();

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
