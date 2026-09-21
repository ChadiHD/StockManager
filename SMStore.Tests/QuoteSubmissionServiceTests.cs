using FluentAssertions;
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
/// What a submit sends to the database, and the three outcomes that are not failures.
/// </summary>
/// <remarks>
/// The price is the thing worth pinning down. It is resolved here, at submit, from the product
/// rows the basket read back — never from the form, because the browser has had the basket page
/// open for as long as it likes and a price in a form is a client's opinion about what things
/// cost. Resolving through <see cref="CatalogPresenter"/> is what makes the quote record what
/// the customer was actually looking at.
/// </remarks>
public class QuoteSubmissionServiceTests
{
    private const int SiteId = 7;
    private const int ContactId = 41;
    private const int BasketId = 9;

    private readonly IQuoteData _quotes = Substitute.For<IQuoteData>();
    private readonly IBasketData _baskets = Substitute.For<IBasketData>();
    private readonly ICatalogData _catalog = Substitute.For<ICatalogData>();
    private readonly ICustomerContext _customer = Substitute.For<ICustomerContext>();

    private QuoteSubmissionService ServiceFor(bool signedIn = true, decimal minMarginPct = 0m)
    {
        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(new SiteModel
        {
            Id = SiteId,
            SiteKey = "test",
            Name = "Test store",
            CurrencyCode = "EUR",
            Locale = "en-IE",
            PriceDisplay = "Public",
            MinMarginPct = minMarginPct,
            OrderMode = "Rfq"
        });
        siteContext.IsResolved.Returns(true);

        _customer.Contact.Returns(signedIn
            ? new ContactModel
            {
                Id = ContactId,
                AccountId = 5,
                FirstName = "Ada",
                LastName = "Byron",
                Email = "ada@example.test",
                Status = "Active",
                AccountStatus = "Approved"
            }
            : null);

        return new QuoteSubmissionService(
            _quotes, _baskets,
            new CatalogPresenter(_catalog, new PriceResolver(), siteContext, _customer),
            siteContext, _customer,
            NullLogger<QuoteSubmissionService>.Instance);
    }

    private static BasketLineModel Line(
        string sku, int quantity = 1, decimal retail = 100m, decimal? cost = 60m,
        bool available = true, int productId = 12) => new()
        {
            ProductId = productId,
            Sku = sku,
            Name = "Widget",
            Quantity = quantity,
            RetailPrice = retail,
            Cost = cost,
            Available = available
        };

    private void BasketHolds(params BasketLineModel[] lines) =>
        _baskets.GetLines(BasketId, SiteId, Arg.Any<int?>()).Returns(lines.ToList());

    private void SubmitSucceeds(string reference = "QT-0041") =>
        _quotes.SubmitRequest(Arg.Any<QuoteRequest>())
            .Returns(new QuoteModel { Id = 3, Reference = reference, AccountId = 5 });

    private QuoteRequest CapturedRequest() =>
        (QuoteRequest)_quotes.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IQuoteData.SubmitRequest))
            .GetArguments()[0]!;

    [Fact]
    public void ASubmitSendsTheContactTheSiteAndTheBasketAndNoAccount()
    {
        BasketHolds(Line("SKU1", quantity: 3));
        SubmitSucceeds();

        ServiceFor().Submit(BasketId, "before month end").Succeeded.Should().BeTrue();

        var request = CapturedRequest();

        request.ContactId.Should().Be(ContactId);
        request.SiteId.Should().Be(SiteId);
        // The basket goes with it so the procedure can empty it in the same transaction.
        request.BasketId.Should().Be(BasketId);
        request.CustomerNote.Should().Be("before month end");
        // No AccountId anywhere on the request: spQuote_SubmitRequest derives it from the
        // contact, because a session proves a contact and nothing else.
        request.Lines.Should().ContainSingle();
        request.Lines[0].Quantity.Should().Be(3);
    }

    [Fact]
    public void ThePriceIsResolvedAtSubmitThroughTheSamePathThatRenderedIt()
    {
        BasketHolds(Line("SKU1", retail: 100m, cost: 95m));
        SubmitSucceeds();

        // A 12% floor over a cost of 95 is 106.40, which is above list — PriceResolver caps it
        // at list rather than charging above it, and the effective discount is therefore zero.
        ServiceFor(minMarginPct: 12m).Submit(BasketId, null);

        var line = CapturedRequest().Lines.Single();

        line.ListPrice.Should().Be(100m);
        line.NetPrice.Should().Be(100m);
        line.DiscountPct.Should().Be(0m);
    }

    [Fact]
    public void AFlooredPriceCarriesItsEffectiveDiscountRatherThanTheGroupsRate()
    {
        BasketHolds(Line("SKU1", retail: 200m, cost: 100m));
        _customer.GroupDiscountPct.Returns(50m);
        SubmitSucceeds();

        // Half off 200 is 100, but a 12% floor over a cost of 100 holds it at 112.00. The
        // discount that produces that is 44%, not the group's 50 — and QuoteLine.DiscountPct is
        // DECIMAL(5, 2) precisely so a value like this survives being stored.
        ServiceFor(minMarginPct: 12m).Submit(BasketId, null);

        var line = CapturedRequest().Lines.Single();

        line.NetPrice.Should().Be(112.00m);
        line.DiscountPct.Should().Be(44.00m);
    }

    [Fact]
    public void AnUnavailableLineIsLeftOutAndNamed()
    {
        BasketHolds(
            Line("GOOD", productId: 12),
            Line("GONE", productId: 13, available: false));
        SubmitSucceeds();

        var outcome = ServiceFor().Submit(BasketId, null);

        outcome.Succeeded.Should().BeTrue();
        CapturedRequest().Lines.Should().ContainSingle()
            .Which.ProductId.Should().Be(12);

        // Dropped rather than refused: the basket page has already warned, and a submit that
        // refuses until the customer tidies up puts the store's supply problem in their way at
        // the moment they were ready to buy. Named, because dropping it silently is the one
        // thing this must not do.
        outcome.DroppedSkus.Should().Equal("GONE");
    }

    [Fact]
    public void ABasketWhereNothingCanBeSuppliedProducesNoQuote()
    {
        BasketHolds(Line("GONE", available: false));

        var outcome = ServiceFor().Submit(BasketId, null);

        outcome.Succeeded.Should().BeFalse();
        outcome.HadAnythingToQuote.Should().BeFalse();
        outcome.DroppedSkus.Should().Equal("GONE");

        // Distinct from a failure, and distinct from a success with nothing in it. A quote with
        // no lines is a request sales cannot answer.
        _quotes.DidNotReceive().SubmitRequest(Arg.Any<QuoteRequest>());
    }

    [Fact]
    public void AnAnonymousVisitorIsNotSignedInRatherThanRefused()
    {
        BasketHolds(Line("SKU1"));

        var outcome = ServiceFor(signedIn: false).Submit(BasketId, null);

        outcome.SignedIn.Should().BeFalse();
        outcome.Succeeded.Should().BeFalse();

        // Not a failure: a quote belongs to an account and a session is the only thing that
        // names one, so the endpoint sends them to sign in and their basket follows.
        _quotes.DidNotReceive().SubmitRequest(Arg.Any<QuoteRequest>());
        _baskets.DidNotReceive().GetLines(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>());
    }

    [Fact]
    public void AWriteThatThrowsIsReportedAsAFailureAndNotRethrown()
    {
        BasketHolds(Line("SKU1"));
        _quotes.SubmitRequest(Arg.Any<QuoteRequest>())
            .Returns(_ => throw new InvalidOperationException("database asleep"));

        var outcome = ServiceFor().Submit(BasketId, null);

        // The procedure is all-or-nothing, so there is no quote and the customer still has
        // their basket. Retrying is the right next move, and an error page does not say so.
        outcome.Succeeded.Should().BeFalse();
        outcome.SignedIn.Should().BeTrue();
        outcome.HadAnythingToQuote.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNoteIsSentAsNothing(string? note)
    {
        BasketHolds(Line("SKU1"));
        SubmitSucceeds();

        ServiceFor().Submit(BasketId, note);

        CapturedRequest().CustomerNote.Should().BeNull();
    }
}
