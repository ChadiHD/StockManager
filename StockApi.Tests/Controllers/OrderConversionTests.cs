using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// How converting a quote is reported, which is a different question from whether it worked.
/// </summary>
/// <remarks>
/// The distinction being defended is the same one <see cref="Feeds.FeedSyncReportingTests"/>
/// defends for a feed sync: a quote somebody else already decided is not a failed request. Once
/// a customer can accept their own quote, an admin pressing Convert a moment later is the
/// ordinary race, and answering it as a fault sends somebody looking for a problem that is not
/// there.
///
/// Also here: that the placer comes from the token. <c>Purchase.StaffId</c> is a foreign key
/// into <c>dbo.[User]</c>, so a value taken from the request body would be an audit trail
/// written by the auditee — and one naming nobody fails in the database after the caller has
/// been told it succeeded, which is exactly how <c>Account.ApprovedBy</c> shipped broken.
/// </remarks>
public class OrderConversionTests
{
    private const string StaffId = "0f4b-operator-id";

    private readonly IOrderData _orders = Substitute.For<IOrderData>();
    private readonly IQuoteData _quotes = Substitute.For<IQuoteData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly OrderController _controller;

    public OrderConversionTests()
    {
        _site.SiteId.Returns(42);

        _quotes.GetQuoteByReference("QT-0041", 42).Returns(new QuoteModel
        {
            Id = 7,
            Reference = "QT-0041",
            AccountId = 5,
            Currency = "EUR",
            Status = QuoteStatus.Priced
        });

        _controller = new OrderController(_orders, _site)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            // NameIdentifier, not Name: only the id is storable in StaffId.
                            new Claim(ClaimTypes.NameIdentifier, StaffId),
                            new Claim(ClaimTypes.Name, "operator@example.com")
                        ], authenticationType: "Test"))
                }
            }
        };
    }

    private static OrderModel Order() => new()
    {
        Id = 3,
        Reference = "SO-0012",
        AccountId = 5,
        Currency = "EUR",
        Status = "Awaiting payment",
        FromQuoteReference = "QT-0041"
    };

    [Fact]
    public void ConversionCreditsTheStaffMemberFromTheTokenAndNobodyElse()
    {
        _orders.ConvertQuoteToOrder(7, Arg.Any<QuoteAcceptance>(), 42)
            .Returns(QuoteAcceptanceResult.Converted(Order()));

        _controller.CreateFromQuote(new OrderController.ConvertQuoteModel("QT-0041"), _quotes);

        var acceptance = (QuoteAcceptance)_orders.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IOrderData.ConvertQuoteToOrder))
            .GetArguments()[1]!;

        acceptance.StaffId.Should().Be(StaffId);
        acceptance.PlacedByContactId.Should().BeNull(
            "an admin conversion is placed by staff, and CK_Purchase_Placer allows exactly one");
    }

    [Fact]
    public void ThePurchaseOrderNumberReachesTheConversion()
    {
        _orders.ConvertQuoteToOrder(7, Arg.Any<QuoteAcceptance>(), 42)
            .Returns(QuoteAcceptanceResult.Converted(Order()));

        _controller.CreateFromQuote(
            new OrderController.ConvertQuoteModel("QT-0041", "PO-99123"), _quotes);

        var acceptance = (QuoteAcceptance)_orders.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IOrderData.ConvertQuoteToOrder))
            .GetArguments()[1]!;

        // The customer's reference, not ours. Many B2B buyers cannot pay an invoice that does
        // not carry it, so losing it here means chasing it by phone later.
        acceptance.PoNumber.Should().Be("PO-99123");
    }

    [Fact]
    public void AQuoteSomebodyElseDecidedAnswersConflictRatherThanSuccess()
    {
        _orders.ConvertQuoteToOrder(7, Arg.Any<QuoteAcceptance>(), 42)
            .Returns(QuoteAcceptanceResult.AlreadyDecided());

        var result = _controller.CreateFromQuote(
            new OrderController.ConvertQuoteModel("QT-0041"), _quotes);

        // Not a 200 with a null body and not a 502: nothing is wrong with the request or the
        // quote. The same reasoning as spAccount_Approve's no-op answering Conflict — reporting
        // success would show a conversion this request did not make.
        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public void AQuoteBelongingToAnotherStoreIsNotFoundRatherThanForbidden()
    {
        var result = _controller.CreateFromQuote(
            new OrderController.ConvertQuoteModel("QT-9999"), _quotes);

        // References are sequential, so a distinguishable refusal would confirm which ones
        // exist in other stores. Nothing is converted either.
        result.Result.Should().BeOfType<NotFoundResult>();
        _orders.DidNotReceive().ConvertQuoteToOrder(
            Arg.Any<int>(), Arg.Any<QuoteAcceptance>(), Arg.Any<int>());
    }

    [Fact]
    public void ASuccessfulConversionReturnsTheOrder()
    {
        _orders.ConvertQuoteToOrder(7, Arg.Any<QuoteAcceptance>(), 42)
            .Returns(QuoteAcceptanceResult.Converted(Order()));

        var result = _controller.CreateFromQuote(
            new OrderController.ConvertQuoteModel("QT-0041"), _quotes);

        result.Value!.Reference.Should().Be("SO-0012");
    }
}

/// <summary>
/// What a quote's status is allowed to be set to.
/// </summary>
/// <remarks>
/// <c>CK_Quote_Status</c> landed in T5 because the accept path gates on the status, and a
/// constraint violation raised inside a stored procedure reaches the caller as a 500 rather
/// than as an answer. Before the constraint, <c>spQuote_UpdateStatus</c> stored whatever string
/// arrived and a typo produced a quote that matched no filter and no step in the progress bar.
/// </remarks>
public class QuoteStatusTests
{
    private readonly IQuoteData _quotes = Substitute.For<IQuoteData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly QuoteController _controller;

    public QuoteStatusTests()
    {
        _site.SiteId.Returns(42);

        _quotes.GetQuoteByReference("QT-0041", 42).Returns(new QuoteModel
        {
            Id = 7,
            Reference = "QT-0041",
            AccountId = 5,
            Status = QuoteStatus.Requested
        });

        _controller = new QuoteController(_quotes, _site);
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Priced")]
    [InlineData("Accepted")]
    [InlineData("Rejected")]
    public void EveryStatusTheConstraintAllowsIsAccepted(string status)
    {
        _controller.UpdateStatus("QT-0041", new QuoteController.QuoteStatusModel(status))
            .Should().BeOfType<NoContentResult>();

        _quotes.Received(1).UpdateStatus(7, status, 42);
    }

    [Theory]
    // Stricter than CK_Quote_Status, which compares under the database collation and would
    // accept "priced". The portal filters and the progress bar compare with == in C#, so a
    // differently-cased status the database accepted matches no filter and no step.
    [InlineData("priced")]
    [InlineData("Invoiced")]
    [InlineData("")]
    public void AStatusTheConstraintWouldRefuseIsRefusedHereFirst(string status)
    {
        var result = _controller.UpdateStatus("QT-0041", new QuoteController.QuoteStatusModel(status));

        result.Should().BeOfType<BadRequestObjectResult>();

        // And the quote is not even looked up — nothing reaches the procedure that would throw.
        _quotes.DidNotReceive().UpdateStatus(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>());
    }
}
