using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using StockManager.Notifications;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// Approve and Reject are the one place a trading application's decision is recorded — see the
/// remarks on AccountController. Everything here is about what makes that decision trail
/// trustworthy: who is credited with it, that a stale or repeated decision is refused, and that
/// a notification which fails to send cannot undo a decision that has already committed.
/// </summary>
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
                new[] { new Claim(ClaimTypes.Name, name) }, authenticationType: "Test"));

    /// <summary>Wires a controller against substitutes, with the account behind id 5 fixed up front.</summary>
    private sealed class Fixture
    {
        public IAccountData Accounts { get; } = Substitute.For<IAccountData>();
        public IEmailSender Email { get; } = Substitute.For<IEmailSender>();
        public AccountController Controller { get; }

        public Fixture(AccountModel? account, string? approverName = "reviewer@example.com")
        {
            var site = Substitute.For<IAdminSiteContext>();
            site.SiteId.Returns(Site().Id);
            site.Site.Returns(Site());

            Accounts.GetAccountById(Arg.Any<int>(), Site().Id).Returns(account);

            Controller = new AccountController(Accounts, site, Email, NullLogger<AccountController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = Principal(approverName) }
                }
            };
        }
    }

    [Fact]
    public async Task ApproveReturnsNotFoundWhenTheAccountIsNotThisStores()
    {
        var fixture = new Fixture(account: null);

        var result = await fixture.Controller.Approve(
            5, new AccountController.ApprovalModel(null), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        fixture.Accounts.DidNotReceive().Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>());
        await fixture.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveReturnsConflictWhenTheAccountWasAlreadyDecided()
    {
        var fixture = new Fixture(Account());
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(false);

        var result = await fixture.Controller.Approve(
            5, new AccountController.ApprovalModel(null), CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
        await fixture.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveRecordsTheApproverFromTheAuthenticatedUserNotTheRequestBody()
    {
        // ApprovalModel carries only a customer group id — there is no field in the request a
        // caller could use to name their own approver even if they wanted to.
        var fixture = new Fixture(Account(), approverName: "alice@store.example");
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(true);

        await fixture.Controller.Approve(5, new AccountController.ApprovalModel(3), CancellationToken.None);

        fixture.Accounts.Received(1).Approve(5, "alice@store.example", 3, 42);
    }

    [Fact]
    public async Task ApproveRecordsUnknownWhenTheRequestHasNoAuthenticatedName()
    {
        var fixture = new Fixture(Account(), approverName: null);
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(true);

        await fixture.Controller.Approve(5, new AccountController.ApprovalModel(null), CancellationToken.None);

        fixture.Accounts.Received(1).Approve(5, "unknown", null, 42);
    }

    [Fact]
    public async Task ApproveSendsTheApprovalEmailOnSuccess()
    {
        var account = Account();
        var fixture = new Fixture(account);
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(true);

        var result = await fixture.Controller.Approve(
            5, new AccountController.ApprovalModel(null), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await fixture.Email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To == account.Email), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveSucceedsEvenWhenSendingTheEmailThrows()
    {
        // The write has already committed by the time NotifyAsync runs — see its remarks on
        // AccountController. Failing the request here would have the portal retry an approval
        // that already happened, landing on the Conflict above and reading as a bug.
        var fixture = new Fixture(Account());
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(true);
        fixture.Email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mail server unreachable"));

        var result = await fixture.Controller.Approve(
            5, new AccountController.ApprovalModel(null), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task ApproveDoesNotAttemptToEmailAnAccountWithNoAddress()
    {
        var fixture = new Fixture(Account(email: ""));
        fixture.Accounts.Approve(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(true);

        var result = await fixture.Controller.Approve(
            5, new AccountController.ApprovalModel(null), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await fixture.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectReturnsBadRequestForABlankReason(string reason)
    {
        var fixture = new Fixture(Account());

        var result = await fixture.Controller.Reject(
            5, new AccountController.RejectionModel(reason), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        // The reason is checked before the account is even looked up — see the remarks on Reject.
        fixture.Accounts.DidNotReceive().GetAccountById(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task RejectReturnsNotFoundWhenTheAccountIsNotThisStores()
    {
        var fixture = new Fixture(account: null);

        var result = await fixture.Controller.Reject(
            5, new AccountController.RejectionModel("Not enough evidence."), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task RejectReturnsConflictWhenTheAccountWasAlreadyDecided()
    {
        var fixture = new Fixture(Account());
        fixture.Accounts.Reject(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(false);

        var result = await fixture.Controller.Reject(
            5, new AccountController.RejectionModel("Not enough evidence."), CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
        await fixture.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectRecordsTheApproverFromTheAuthenticatedUserNotTheRequestBody()
    {
        var fixture = new Fixture(Account(), approverName: "alice@store.example");
        fixture.Accounts.Reject(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(true);

        await fixture.Controller.Reject(
            5, new AccountController.RejectionModel("Not enough evidence."), CancellationToken.None);

        fixture.Accounts.Received(1).Reject(5, "alice@store.example", "Not enough evidence.", 42);
    }

    [Fact]
    public async Task RejectSendsTheRejectionEmailQuotingTheReasonOnSuccess()
    {
        var account = Account();
        var fixture = new Fixture(account);
        fixture.Accounts.Reject(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(true);

        var result = await fixture.Controller.Reject(
            5, new AccountController.RejectionModel("Not enough evidence."), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await fixture.Email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To == account.Email && m.Body.Contains("Not enough evidence.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectSucceedsEvenWhenSendingTheEmailThrows()
    {
        var fixture = new Fixture(Account());
        fixture.Accounts.Reject(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(true);
        fixture.Email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mail server unreachable"));

        var result = await fixture.Controller.Reject(
            5, new AccountController.RejectionModel("Not enough evidence."), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }
}
