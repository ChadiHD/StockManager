using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// Approve and Reject are the one place a trading application's decision is recorded — see the
/// remarks on AccountController. Everything here is about what makes that decision trail
/// trustworthy: who is credited with it, and that a stale or repeated decision is refused.
/// </summary>
/// <remarks>
/// The mail that follows a decision is no longer this controller's. <c>spAccount_Approve</c>
/// and <c>spAccount_Reject</c> queue it in the same transaction, so whether a decision tells
/// the applicant is a database question and <c>AccountDecisionMailTests</c> asks it there. What
/// used to be tested here — that a failing mail server cannot fail an approval — is now true
/// by construction: nothing on this path talks to one.
/// </remarks>
public class AccountControllerTests
{
    private static SiteModel Site() => new()
    {
        Id = 42,
        SiteKey = "test-store",
        Name = "Test Store",
        Domain = "test-store.example",
        Country = "IE",
        CurrencyCode = "EUR",
        Locale = "en-IE",
        OrderMode = "Rfq",
        RegistrationFieldSet = "eu-b2b",
        PriceDisplay = "Public",
        IsActive = true
    };

    private static AccountModel Account(string email = "buyer@example.com") => new()
    {
        Id = 5,
        Reference = "TA-0005",
        Company = "Acme Trading",
        ContactName = "Ada Byron",
        Email = email,
        Country = "IE",
        Currency = "EUR",
        Status = "Pending"
    };

    private static ClaimsPrincipal Principal(string? name) =>
        name is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(
                [
                    // NameIdentifier, not Name. Account.ApprovedBy is a foreign key into
                    // dbo.User(UserId), so only the id is storable — see AccountController.Decider.
                    new Claim(ClaimTypes.NameIdentifier, name),
                    new Claim(ClaimTypes.Name, "operator@example.com")
                ], authenticationType: "Test"));

    /// <summary>Wires a controller against substitutes, with the account behind id 5 fixed up front.</summary>
    private sealed class Fixture
    {
        public IAccountData Accounts { get; } = Substitute.For<IAccountData>();
        public AccountController Controller { get; }

        public Fixture(AccountModel? account, string? approverId = "0f4b-reviewer-id")
        {
            var site = Substitute.For<IAdminSiteContext>();
            site.SiteId.Returns(Site().Id);
            site.Site.Returns(Site());

            Accounts.GetAccountById(Arg.Any<int>(), Site().Id).Returns(account);

            Controller = new AccountController(Accounts, site, NullLogger<AccountController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = Principal(approverId) }
                }
            };
        }
    }

    [Fact]
    public void ApproveReturnsNotFoundWhenTheAccountIsNotThisStores()
    {
        var fixture = new Fixture(account: null);

        var result = fixture.Controller.Approve(5, new AccountController.ApprovalModel(null));

        result.Should().BeOfType<NotFoundResult>();
        fixture.Accounts.DidNotReceive().Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>());
    }

    [Fact]
    public void ApproveReturnsConflictWhenTheAccountWasAlreadyDecided()
    {
        var fixture = new Fixture(Account());
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(false);

        var result = fixture.Controller.Approve(5, new AccountController.ApprovalModel(null));

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public void ApproveRecordsTheApproverFromTheAuthenticatedUserNotTheRequestBody()
    {
        // ApprovalModel carries only a customer group id — there is no field in the request a
        // caller could use to name their own approver even if they wanted to.
        var fixture = new Fixture(Account(), approverId: "alice-user-id");
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(true);

        fixture.Controller.Approve(5, new AccountController.ApprovalModel(3));

        fixture.Accounts.Received(1).Approve(5, "alice-user-id", 3, 42);
    }

    [Fact]
    public void ApproveRefusesRatherThanRecordingAnApproverItCannotStore()
    {
        /*
        This test used to assert the opposite — that a principal with no id was recorded as
        "unknown" — and that was the bug, not the behaviour. Account.ApprovedBy is a foreign
        key into dbo.User(UserId), so "unknown" is as unstorable as an email address: the
        UPDATE fails with error 547 and the approval is lost after the caller has been told it
        succeeded. Refusing is the only honest answer, and [Authorize] should mean it never
        comes up.
        */
        var fixture = new Fixture(Account(), approverId: null);
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(true);

        var result = fixture.Controller.Approve(5, new AccountController.ApprovalModel(null));

        result.Should().BeOfType<UnauthorizedObjectResult>();
        fixture.Accounts.DidNotReceive().Approve(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>());
    }

    [Theory]
    [InlineData("buyer@example.com")]
    // An account keyed in by hand may have no address. The decision still stands; the
    // procedure queues nothing, and the controller's warning is the record of that.
    [InlineData("")]
    public void ApproveSucceedsWhetherOrNotThereIsAnyoneToTell(string email)
    {
        var fixture = new Fixture(Account(email));
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(true);

        var result = fixture.Controller.Approve(5, new AccountController.ApprovalModel(null));

        result.Should().BeOfType<NoContentResult>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectReturnsBadRequestForABlankReason(string reason)
    {
        var fixture = new Fixture(Account());

        var result = fixture.Controller.Reject(5, new AccountController.RejectionModel(reason));

        result.Should().BeOfType<BadRequestObjectResult>();
        // The reason is checked before the account is even looked up — see the remarks on Reject.
        fixture.Accounts.DidNotReceive().GetAccountById(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void RejectReturnsNotFoundWhenTheAccountIsNotThisStores()
    {
        var fixture = new Fixture(account: null);

        var result = fixture.Controller.Reject(5, new AccountController.RejectionModel("Not enough evidence."));

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void RejectReturnsConflictWhenTheAccountWasAlreadyDecided()
    {
        var fixture = new Fixture(Account());
        fixture.Accounts.Reject(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(false);

        var result = fixture.Controller.Reject(5, new AccountController.RejectionModel("Not enough evidence."));

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public void RejectRecordsTheApproverFromTheAuthenticatedUserNotTheRequestBody()
    {
        var fixture = new Fixture(Account(), approverId: "alice-user-id");
        fixture.Accounts.Reject(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(true);

        var result = fixture.Controller.Reject(5, new AccountController.RejectionModel("  Not enough evidence. "));

        result.Should().BeOfType<NoContentResult>();
        // Trimmed, because it is quoted to the applicant verbatim.
        fixture.Accounts.Received(1).Reject(5, "alice-user-id", "Not enough evidence.", 42);
    }
}
