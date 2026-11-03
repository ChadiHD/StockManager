using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Controllers;

// Approve and Reject record who decided and tell the applicant. The status endpoint does
// neither, so it used to be a way to approve an application with no record of it — and a create
// could do the same by naming its own status.
public class AccountStatusGuardTests
{
    private readonly IAccountData _accounts = Substitute.For<IAccountData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly AccountController _controller;

    public AccountStatusGuardTests()
    {
        _site.SiteId.Returns(42);
        _controller = new AccountController(_accounts, _site, NullLogger<AccountController>.Instance);
    }

    [Theory]
    [InlineData("Approved", "Suspended")]
    [InlineData("Suspended", "Approved")]
    public void SuspendingAndReinstatingStillWork(string from, string to)
    {
        _accounts.GetAccountById(5, 42).Returns(new AccountModel { Id = 5, Status = from });

        _controller.UpdateStatus(5, new AccountController.StatusChangeModel(to)).Should().BeOfType<NoContentResult>();

        _accounts.Received(1).UpdateStatus(5, to, 42);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Rejected")]
    public void AnApplicationIsNotApprovedOrSuspendedFromHere(string from)
    {
        _accounts.GetAccountById(5, 42).Returns(new AccountModel { Id = 5, Status = from });

        _controller.UpdateStatus(5, new AccountController.StatusChangeModel("Approved")).Should().BeOfType<ConflictObjectResult>();

        _accounts.DidNotReceiveWithAnyArgs().UpdateStatus(default, default!, default);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Rejected")]
    [InlineData("approved")]
    public void OnlyApprovedAndSuspendedAreSetHere(string to)
    {
        _accounts.GetAccountById(5, 42).Returns(new AccountModel { Id = 5, Status = "Approved" });

        _controller.UpdateStatus(5, new AccountController.StatusChangeModel(to)).Should().BeOfType<BadRequestObjectResult>();

        _accounts.DidNotReceiveWithAnyArgs().UpdateStatus(default, default!, default);
    }

    [Fact]
    public void ACreatedAccountWaitsForApprovalWhateverTheBodySays()
    {
        _controller.Create(new AccountModel { Company = "Acme", Status = "Approved" });

        _accounts.Received(1).CreateAccount(Arg.Is<AccountModel>(account => account.Status == "Pending"), 42);
    }
}
