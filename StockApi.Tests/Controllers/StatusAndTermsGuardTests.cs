using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using StockManager.Notifications;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// The two boundaries that now refuse a value the database would refuse anyway.
/// </summary>
/// <remarks>
/// Both exist for the reason <c>QuoteController</c>'s status guard exists: a
/// <c>CHECK</c> violation raised inside a stored procedure reaches the caller as a 500 rather
/// than as an answer, so the API has to say no first. What is worth asserting is not only
/// that a bad value is a 400, but that it never reaches the data layer — a guard that refuses
/// after writing is not a guard.
/// </remarks>
public class OrderStatusGuardTests
{
    private readonly IOrderData _orders = Substitute.For<IOrderData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly OrderController _controller;

    public OrderStatusGuardTests()
    {
        _site.SiteId.Returns(42);

        _orders.GetOrderByReference("SO-0012", 42).Returns(new OrderModel
        {
            Id = 9, Reference = "SO-0012", AccountId = 5, Status = OrderStatus.AwaitingPayment
        });

        _controller = new OrderController(_orders, _site);
    }

    [Theory]
    [InlineData("Awaiting payment")]
    [InlineData("Processing")]
    [InlineData("Fulfilled")]
    [InlineData("Cancelled")]
    public void EveryStatusTheConstraintAllowsIsAccepted(string status)
    {
        _controller.UpdateStatus("SO-0012", new OrderController.OrderStatusModel(status))
            .Should().BeOfType<NoContentResult>();

        _orders.Received(1).UpdateStatus(9, status, 42);
    }

    [Theory]
    [InlineData("fulfilled")]
    [InlineData("Shipped")]
    [InlineData("")]
    public void AStatusTheConstraintWouldRefuseIsRefusedHereFirst(string status)
    {
        _controller.UpdateStatus("SO-0012", new OrderController.OrderStatusModel(status))
            .Should().BeOfType<BadRequestObjectResult>();

        // And the order is not even looked up, so nothing reaches the procedure that throws.
        _orders.DidNotReceive().UpdateStatus(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>());
    }
}

/// <summary>
/// Payment terms at the API boundary. <c>AccountData</c> derives the day count from the
/// label, so an unrecognised label would derive zero and fail <c>CK_Account_Terms</c> inside
/// the procedure — this is what turns that 500 into an answer.
/// </summary>
public class PaymentTermsGuardTests
{
    private readonly IAccountData _accounts = Substitute.For<IAccountData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly AccountController _controller;

    public PaymentTermsGuardTests()
    {
        _site.SiteId.Returns(42);

        _accounts.GetAccountById(5, 42).Returns(new AccountModel
        {
            Id = 5, Reference = "AC-0005", Company = "Acme Trading", Status = "Approved"
        });

        _controller = new AccountController(_accounts, _site, Substitute.For<IEmailSender>(), NullLogger<AccountController>.Instance);
    }

    [Theory]
    [InlineData("Prepaid")]
    [InlineData("Net 14")]
    [InlineData("Net 30")]
    [InlineData("Net 45")]
    [InlineData("Net 60")]
    public void EveryTermThePortalOffersIsAccepted(string terms)
    {
        _controller.UpdateTerms(5, new AccountController.TermsChangeModel(null, "Credit", terms, 10000m))
            .Should().BeOfType<NoContentResult>();

        _accounts.Received(1).UpdateTerms(5, null, "Credit", terms, 10000m, 42);
    }

    [Theory]
    [InlineData("NET30")]
    [InlineData("30 days")]
    [InlineData("")]
    public void ATermNoScreenOffersIsRefusedBeforeTheWrite(string terms)
    {
        _controller.UpdateTerms(5, new AccountController.TermsChangeModel(null, "Credit", terms, 10000m))
            .Should().BeOfType<BadRequestObjectResult>();

        _accounts.DidNotReceive().UpdateTerms(
            Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<int>());
    }
}
