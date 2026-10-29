using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Email;
using SMDataManager.Library.Models;
using SMDataManager.Library.Pricing;
using SMDataManager.Library.Tax;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Ordering;
using SMStore.Sites;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// What a checkout sends to the database and the outbox (T12).
/// </summary>
/// <remarks>
/// The price, the tax and the guest's link are the things worth pinning down. The price comes
/// from the catalog, never the form; the tax from where the goods go; and the link is the only way
/// a guest gets back to their order, so it has to reach the mail and the page as one token.
/// </remarks>
public class CheckoutServiceTests
{
    private const int SiteId = 7;
    private const int BasketId = 9;
    private const string CartToken = "cart-token-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly IOrderData _orders = Substitute.For<IOrderData>();
    private readonly IBasketData _baskets = Substitute.For<IBasketData>();
    private readonly IAccountData _accounts = Substitute.For<IAccountData>();
    private readonly ICatalogData _catalog = Substitute.For<ICatalogData>();
    private readonly IEmailOutbox _outbox = Substitute.For<IEmailOutbox>();
    private readonly ICustomerContext _customer = Substitute.For<ICustomerContext>();
    private readonly DefaultHttpContext _http = new();
    private OrderPlacement? _placed;

    private static readonly CheckoutForm Guest = new(
        "ana@example.test", "Ana", "Silva", null, "Rua A, 10", null, "São Paulo", null, null, "US");

    private CheckoutService ServiceFor(ContactModel? contact = null)
    {
        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(new SiteModel
        {
            Id = SiteId, SiteKey = "shop", Name = "Shop", Domain = "shop.test", CurrencyCode = "EUR",
            Locale = "en-IE", PriceDisplay = "Public", OrderMode = "DirectCheckout",
            Country = "IE", TaxRuleSet = "eu-b2b", StandardTaxRatePct = 23m
        });
        siteContext.IsResolved.Returns(true);

        _customer.Contact.Returns(contact);
        _customer.AccountId.Returns(contact?.AccountId);

        _http.Request.Headers.Cookie = $"{BasketToken.CookieName}={CartToken}";
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(_http);

        _baskets.FindBasket(SiteId, CartToken, contact?.Id).Returns(new BasketModel { Id = BasketId });
        _baskets.FindBasket(SiteId, null, contact?.Id).Returns(new BasketModel { Id = BasketId });
        _baskets.GetLines(BasketId, SiteId, Arg.Any<int?>()).Returns(
        [
            new BasketLineModel { ProductId = 12, Sku = "SHIRT-M", Name = "Shirt", Quantity = 2, RetailPrice = 40m, Cost = 10m, Available = true },
            new BasketLineModel { ProductId = 13, Sku = "GONE", Name = "Gone", Quantity = 1, RetailPrice = 5m, Available = false }
        ]);

        _orders.PlaceFromBasket(Arg.Do<OrderPlacement>(placement => _placed = placement))
            .Returns(new OrderPlacementResult { Reference = "SO-0042" });
        _orders.GetOrderByGuestToken(Arg.Any<string>(), SiteId).Returns(Order());
        _orders.GetOrderForAccount("SO-0042", 5, SiteId).Returns(Order());

        var presenter = new CatalogPresenter(_catalog, new PriceResolver(), siteContext, _customer);

        return new CheckoutService(
            _orders, _baskets, _accounts,
            new BasketService(_baskets, presenter, siteContext, _customer, accessor, NullLogger<BasketService>.Instance),
            presenter,
            new TaxAssessor(new TaxRuleSetProvider(new ITaxRuleSet[] { new EuB2bTaxRuleSet() })),
            _outbox, siteContext, _customer, NullLogger<CheckoutService>.Instance);
    }

    private static OrderModel Order() => new()
    {
        Id = 3, Reference = "SO-0042", AccountId = 5, SubTotal = 80m, VAT = 0m, FinalPrice = 80m,
        TaxTreatment = "Export", TaxLegend = "Zero-rated export."
    };

    [Fact]
    public void AGuestsOrderIsPricedByTheCatalogTaxedWhereItGoesAndMailedItsLink()
    {
        var outcome = ServiceFor().Place(Guest);

        outcome.Succeeded.Should().BeTrue();
        _placed!.ContactId.Should().BeNull();
        _placed.Email.Should().Be("ana@example.test");
        _placed.Lines.Should().ContainSingle("the line the store can no longer supply is left out")
            .Which.NetPrice.Should().Be(40m);

        // An Irish store delivering to the United States exports, whatever the store's own rate.
        _placed.Delivery.Country.Should().Be("US");
        _placed.Tax.RatePct.Should().Be(0m);

        BasketToken.IsWellFormed(_placed.GuestToken).Should().BeTrue();
        outcome.GuestToken.Should().Be(_placed.GuestToken);
        _outbox.Received(1).Enqueue(SiteId, "ana@example.test", "Ana Silva", EmailTemplates.OrderPlaced,
            Arg.Is<OrderPlacedPayload>(payload => payload.GuestToken == _placed.GuestToken && payload.Reference == "SO-0042"));

        // The cart it named is gone, so the cookie goes with it.
        _http.Response.Headers.SetCookie.ToString().Should().Contain(BasketToken.CookieName);
    }

    [Fact]
    public void ASignedInCustomerOrdersAsThemselvesWithNoToken()
    {
        var outcome = ServiceFor(new ContactModel { Id = 41, AccountId = 5, FirstName = "Bea", LastName = "Costa", Email = "bea@example.test" })
            .Place(Guest with { Email = "someone.else@example.test" });

        outcome.GuestToken.Should().BeNull();
        _placed!.ContactId.Should().Be(41);
        _placed.GuestToken.Should().BeNull();
        _outbox.Received(1).Enqueue(SiteId, "bea@example.test", Arg.Any<string>(), EmailTemplates.OrderPlaced,
            Arg.Is<OrderPlacedPayload>(payload => payload.GuestToken == null));
    }

    [Fact]
    public void AShortOrderNamesWhatIsShortAndSendsNothing()
    {
        var service = ServiceFor();
        _orders.PlaceFromBasket(Arg.Any<OrderPlacement>()).Returns(new OrderPlacementResult { ShortSkus = "SHIRT-M,SOCKS" });

        var outcome = service.Place(Guest);

        outcome.Succeeded.Should().BeFalse();
        outcome.ShortSkus.Should().Equal("SHIRT-M", "SOCKS");
        _outbox.DidNotReceiveWithAnyArgs().Enqueue<OrderPlacedPayload>(default, default!, default!, default!, default!);
    }

    [Fact]
    public void AFormWithoutAnAddressOrAnAddressToMailPlacesNothing()
    {
        var outcome = ServiceFor().Place(Guest with { Email = "not an address", Line1 = " ", City = null, Country = "XX" });

        outcome.Errors.Keys.Should().BeEquivalentTo(nameof(CheckoutForm.Email), nameof(CheckoutForm.Line1),
            nameof(CheckoutForm.City), nameof(CheckoutForm.Country));
        _orders.DidNotReceiveWithAnyArgs().PlaceFromBasket(default!);
    }
}
