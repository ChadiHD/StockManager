using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// The two actions behind the portal's "Save draft" and "Send to customer".
/// </summary>
/// <remarks>
/// What is worth asserting here is the shape of each answer rather than the write, which
/// <c>QuoteRepricingTests</c> in the library project covers against a real database. Two
/// things in particular: a line the procedure refused is a 404 and not a 204, and a refused
/// price claim is a 409 and not a failure — the distinction the portal renders as a different
/// toast, and the one a substituted data layer is happy to let a controller lose.
/// </remarks>
public class QuoteRepricingTests
{
    private readonly IQuoteData _quotes = Substitute.For<IQuoteData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly QuoteController _controller;

    public QuoteRepricingTests()
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

    [Fact]
    public void AnEditPassesTheQuotesIdAndTheSiteToTheProcedure()
    {
        _quotes.UpdateQuoteLine(7, 31, 5, 7m, 42, null).Returns(true);

        _controller.UpdateLine("QT-0041", 31, new QuoteController.EditQuoteLineModel(5, 7m))
            .Should().BeOfType<NoContentResult>();

        // The reference is resolved to an id here so the line id alone cannot address a line:
        // the same reason the delete resolves it.
        _quotes.Received(1).UpdateQuoteLine(7, 31, 5, 7m, 42, null);
    }

    [Fact]
    public void AStatedNetPriceIsPassedThrough()
    {
        _quotes.UpdateQuoteLine(7, 31, 2, 7m, 42, 88.88m).Returns(true);

        _controller.UpdateLine("QT-0041", 31, new QuoteController.EditQuoteLineModel(2, 7m, 88.88m))
            .Should().BeOfType<NoContentResult>();

        _quotes.Received(1).UpdateQuoteLine(7, 31, 2, 7m, 42, 88.88m);
    }

    [Fact]
    public void ALineTheProcedureRefusedIsNotFoundRatherThanNoContent()
    {
        // False covers both refusals the caller cannot tell apart and does not need to: the
        // line is not on this quote, or the quote is accepted and its lines are an order's
        // record. Answering 204 would report a write that did not happen.
        _quotes.UpdateQuoteLine(7, 31, 5, 7m, 42, null).Returns(false);

        _controller.UpdateLine("QT-0041", 31, new QuoteController.EditQuoteLineModel(5, 7m))
            .Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void AnEditOnAQuoteThisStoreCannotSeeNeverReachesTheProcedure()
    {
        _controller.UpdateLine("QT-9999", 31, new QuoteController.EditQuoteLineModel(5, 7m))
            .Should().BeOfType<NotFoundResult>();

        _quotes.DidNotReceive().UpdateQuoteLine(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(),
            Arg.Any<int>(), Arg.Any<decimal?>());
    }

    [Fact]
    public void SendingToTheCustomerClaimsTheStatusTransition()
    {
        _quotes.Price(7, 42).Returns(true);

        _controller.Price("QT-0041").Should().BeOfType<NoContentResult>();

        _quotes.Received(1).Price(7, 42);
    }

    [Fact]
    public void AQuoteTheCustomerAlreadyDecidedIsAConflictAndNotAFailure()
    {
        _quotes.Price(7, 42).Returns(false);

        // 409 the whole way out, exactly as a refused conversion is. An operator told "that
        // failed" about a customer accepting their own quote learns to discount the message
        // that matters.
        _controller.Price("QT-0041").Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public void PricingAQuoteThisStoreCannotSeeNeverReachesTheProcedure()
    {
        _controller.Price("QT-9999").Should().BeOfType<NotFoundResult>();

        _quotes.DidNotReceive().Price(Arg.Any<int>(), Arg.Any<int>());
    }
}
