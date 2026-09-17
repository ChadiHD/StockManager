using FluentAssertions;
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
/// When a customer may decide their own quote, and who the order records as having placed it.
/// </summary>
/// <remarks>
/// The interesting rules are the ones that are *not* in
/// <c>spOrder_ConvertFromQuote</c>. That procedure lets `Requested` through on purpose — its job
/// is to stop a double conversion, and an admin converting an unpriced quote because the
/// customer rang up is a deliberate act. A customer accepting a price nobody has set is not, so
/// "priced, and not expired" lives here.
/// </remarks>
public class QuoteDecisionServiceTests
{
    private const int SiteId = 7;
    private const int ContactId = 41;
    private const int AccountId = 5;
    private const string Reference = "QT-0041";

    private readonly IQuoteData _quotes = Substitute.For<IQuoteData>();
    private readonly IOrderData _orders = Substitute.For<IOrderData>();
    private readonly ICustomerContext _customer = Substitute.For<ICustomerContext>();

    private QuoteDecisionService Service()
    {
        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(new SiteModel
        {
            Id = SiteId, SiteKey = "test", Name = "Test store", OrderMode = "Rfq"
        });
        siteContext.IsResolved.Returns(true);

        _customer.Contact.Returns(new ContactModel
        {
            Id = ContactId,
            AccountId = AccountId,
            FirstName = "Ada",
            LastName = "Byron",
            Email = "ada@example.test",
            Status = "Active",
            AccountStatus = "Approved"
        });
        _customer.AccountId.Returns(AccountId);

        return new QuoteDecisionService(
            _quotes, _orders, siteContext, _customer,
            NullLogger<QuoteDecisionService>.Instance);
    }

    /// <summary>Makes the reference resolvable for this account, in the given state.</summary>
    private void QuoteIs(string status, DateTime? expires = null) =>
        _quotes.GetQuoteForAccount(Reference, AccountId, SiteId).Returns(new QuoteModel
        {
            Id = 3,
            Reference = Reference,
            AccountId = AccountId,
            Currency = "EUR",
            Status = status,
            ExpiresDate = expires
        });

    private void ConversionProduces(QuoteAcceptanceResult result) =>
        _orders.ConvertQuoteToOrder(3, Arg.Any<QuoteAcceptance>(), SiteId).Returns(result);

    private QuoteAcceptance CapturedAcceptance() =>
        (QuoteAcceptance)_orders.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IOrderData.ConvertQuoteToOrder))
            .GetArguments()[1]!;

    [Fact]
    public void AcceptingAPricedQuoteRecordsTheContactAndReturnsTheOrder()
    {
        QuoteIs(QuoteStatus.Priced, DateTime.UtcNow.AddDays(7));
        ConversionProduces(QuoteAcceptanceResult.Converted(
            new OrderModel { Id = 9, Reference = "SO-0012" }));

        var decision = Service().Accept(Reference, "PO-99123");

        decision.Succeeded.Should().BeTrue();
        decision.OrderReference.Should().Be("SO-0012");

        var acceptance = CapturedAcceptance();

        // The placer is the contact from the session. Purchase.StaffId is a foreign key into
        // dbo.[User], which holds staff, so a customer acceptance has nothing to put there.
        acceptance.PlacedByContactId.Should().Be(ContactId);
        acceptance.StaffId.Should().BeNull();
        acceptance.PoNumber.Should().Be("PO-99123");
    }

    [Fact]
    public void ACustomerCannotAcceptAQuoteNobodyHasPricedYet()
    {
        QuoteIs(QuoteStatus.Requested);

        var decision = Service().Accept(Reference, null);

        // spOrder_ConvertFromQuote would have allowed it: Requested passes there because an
        // admin converting for somebody who rang up is deliberate. This is the rule that says
        // a customer accepting a price nobody set is not.
        decision.Should().BeSameAs(QuoteDecision.NotDecidable);
        _orders.DidNotReceive().ConvertQuoteToOrder(
            Arg.Any<int>(), Arg.Any<QuoteAcceptance>(), Arg.Any<int>());
    }

    [Fact]
    public void AnExpiredQuoteCannotBeAccepted()
    {
        QuoteIs(QuoteStatus.Priced, DateTime.UtcNow.AddDays(-1));

        // Expiry blocks rather than warns, unlike a delisted line. ExpiresDate is a statement
        // the store already made in writing, and honouring it past its date is the store's
        // choice to make, not a button's.
        Service().Accept(Reference, null).Should().BeSameAs(QuoteDecision.NotDecidable);
    }

    [Fact]
    public void AQuoteWithNoExpiryDateIsStillDecidable()
    {
        QuoteIs(QuoteStatus.Priced);
        ConversionProduces(QuoteAcceptanceResult.Converted(
            new OrderModel { Reference = "SO-0012" }));

        // ExpiresDate is nullable and spQuote_Insert accepts a null, so "no date" must not read
        // as "expired" — a quote with no deadline is one the store did not put one on.
        Service().Accept(Reference, null).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("QT-9999")]
    public void AReferenceThatIsNotThisAccountsIsNotYours(string? reference)
    {
        QuoteIs(QuoteStatus.Priced);

        // "Not yours" and "not here" are one answer, because references are sequential and a
        // distinguishable refusal would confirm which ones exist.
        Service().Accept(reference, null).Should().BeSameAs(QuoteDecision.NotYours);
        Service().Reject(reference, "no thanks").Should().BeSameAs(QuoteDecision.NotYours);
    }

    [Fact]
    public void AQuoteSomebodyElseDecidedFirstIsReportedAsSuch()
    {
        QuoteIs(QuoteStatus.Priced);
        ConversionProduces(QuoteAcceptanceResult.AlreadyDecided());

        // A company with two buyers: the other one accepted it while this page was open. The
        // claim in spOrder_ConvertFromQuote is what makes this reachable rather than a second
        // order, and it is not a fault in this request.
        Service().Accept(Reference, null).Should().BeSameAs(QuoteDecision.AlreadyDecided);
    }

    [Fact]
    public void AConversionThatWritesNoOrderIsAFailure()
    {
        QuoteIs(QuoteStatus.Priced);
        ConversionProduces(QuoteAcceptanceResult.Converted(null!));

        Service().Accept(Reference, null).Should().BeSameAs(QuoteDecision.Failed);
    }

    [Fact]
    public void RejectingAPricedQuoteRecordsTheReason()
    {
        QuoteIs(QuoteStatus.Priced);
        _quotes.RejectForAccount(3, AccountId, SiteId, "Lead time too long").Returns(true);

        Service().Reject(Reference, "Lead time too long")
            .Should().BeSameAs(QuoteDecision.Rejected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ARejectionNeedsAReasonAndAsksBeforeWriting(string? reason)
    {
        QuoteIs(QuoteStatus.Priced);

        // The procedure requires one too. Asking here means the customer gets their form back
        // rather than an error, and "they said no" is not an answer to anybody asking what went
        // wrong with the price.
        Service().Reject(Reference, reason).Should().BeSameAs(QuoteDecision.ReasonRequired);
        _quotes.DidNotReceive().RejectForAccount(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public void ARejectionOfAQuoteSomebodyElseDecidedChangesNothing()
    {
        QuoteIs(QuoteStatus.Priced);
        _quotes.RejectForAccount(3, AccountId, SiteId, Arg.Any<string>()).Returns(false);

        Service().Reject(Reference, "Too expensive")
            .Should().BeSameAs(QuoteDecision.AlreadyDecided);
    }

    [Fact]
    public void ARejectionThatThrowsIsAFailureAndNotRethrown()
    {
        QuoteIs(QuoteStatus.Priced);
        _quotes.RejectForAccount(3, AccountId, SiteId, Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("database asleep"));

        Service().Reject(Reference, "Too expensive").Should().BeSameAs(QuoteDecision.Failed);
    }
}
