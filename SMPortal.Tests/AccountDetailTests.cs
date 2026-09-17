using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin.Accounts;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// AccountDetail is where an admin decides whether a company may trade on credit, so the tests
// here weigh more than most: the approve/reject flow must send exactly what the operator chose
// (never a silently different default), a conflict must say so rather than claim success, and
// the approval-history panel -- which "used to invent a plausible-looking timeline" per its own
// comment -- must show only what the account record actually carries.
public class AccountDetailTests : TestContext
{
    private static readonly Group[] Groups =
    {
        new() { Name = "Reseller", Discount = 10 },
        new() { Name = "Retail", Discount = 0 },
    };

    public AccountDetailTests()
    {
        Services.AddSingleton(Substitute.For<IToastService>());
    }

    private IToastService Toast => Services.GetRequiredService<IToastService>();

    private static Account NewAccount(
        string status = "Pending",
        string group = "Reseller",
        string decidedBy = "",
        string decidedOn = "",
        string rejectionReason = "") => new()
    {
        Id = "AC-001",
        Company = "Acme Trading",
        Country = "Ireland",
        Currency = "EUR",
        Since = "2026-01-05",
        Group = group,
        Payment = "Card",
        Terms = "Prepaid",
        Status = status,
        DecidedBy = decidedBy,
        DecidedOn = decidedOn,
        RejectionReason = rejectionReason,
    };

    private static IAdminDataService MockDataFor(Account account, IReadOnlyList<AccountDocument>? documents = null)
    {
        var data = Substitute.For<IAdminDataService>();
        data.Groups.Returns(Groups);
        data.DiscountFor(Arg.Any<string>())
            .Returns(ci => Groups.FirstOrDefault(g => g.Name == ci.Arg<string>())?.Discount ?? 0);
        data.GetAccount(account.Id).Returns(_ => account);
        data.GetContacts(account.Id).Returns(Task.FromResult<IReadOnlyList<Contact>>(Array.Empty<Contact>()));
        data.GetAddresses(account.Id).Returns(Task.FromResult<IReadOnlyList<AccountAddress>>(Array.Empty<AccountAddress>()));
        data.GetDocuments(account.Id).Returns(Task.FromResult(documents ?? (IReadOnlyList<AccountDocument>)Array.Empty<AccountDocument>()));
        return data;
    }

    private IRenderedComponent<AccountDetail> Render(IAdminDataService data, string id = "AC-001")
    {
        Services.AddSingleton(data);
        return RenderComponent<AccountDetail>(p => p.Add(x => x.Id, id));
    }

    [Theory]
    [InlineData("Pending", true)]
    [InlineData("Approved", false)]
    [InlineData("Suspended", false)]
    [InlineData("Rejected", false)]
    public void ShowsTheApproveRejectBannerOnlyWhileTheAccountIsPending(string status, bool shouldShow)
    {
        var cut = Render(MockDataFor(NewAccount(status: status)));

        cut.FindAll(".approve-banner").Should().HaveCount(shouldShow ? 1 : 0);
    }

    [Fact]
    public async Task DefaultsTheApprovalGroupToTheExistingGroupAndApprovingUnchangedKeepsIt()
    {
        var account = NewAccount(group: "Retail");
        var data = MockDataFor(account);
        data.ApproveAccount(account.Id, Arg.Any<string?>()).Returns(ci =>
        {
            account.Status = "Approved";
            account.Group = ci.ArgAt<string?>(1) ?? account.Group;
            return Task.FromResult<string?>(null);
        });
        var cut = Render(data);

        await cut.Find(".approve-banner button.btn-accent").ClickAsync(new MouseEventArgs());

        ((IHtmlSelectElement)cut.Find(".modal-body select.input")).Value.Should().Be("Retail",
            "the modal must default to the group already on the account, not the first group in the list");

        await cut.Find(".modal-foot button.btn-accent").ClickAsync(new MouseEventArgs());

        await data.Received(1).ApproveAccount("AC-001", "Retail");
        Toast.Received(1).Show("Account approved");
        cut.FindAll(".approve-banner").Should().BeEmpty("the account is no longer Pending once approval succeeds");
    }

    [Fact]
    public async Task ApprovingWithADifferentChosenGroupSendsThatGroupRatherThanTheDefault()
    {
        var account = NewAccount(group: "Retail");
        var data = MockDataFor(account);
        data.ApproveAccount(account.Id, Arg.Any<string?>()).Returns(Task.FromResult<string?>(null));
        var cut = Render(data);

        await cut.Find(".approve-banner button.btn-accent").ClickAsync(new MouseEventArgs());
        await cut.Find(".modal-body select.input").ChangeAsync(new ChangeEventArgs { Value = "Reseller" });
        await cut.Find(".modal-foot button.btn-accent").ClickAsync(new MouseEventArgs());

        await data.Received(1).ApproveAccount("AC-001", "Reseller");
        await data.DidNotReceive().ApproveAccount("AC-001", "Retail");
    }

    [Fact]
    public async Task ShowsTheConflictMessageAndDoesNotClaimSuccessWhenApprovalIsRefused()
    {
        var account = NewAccount();
        var data = MockDataFor(account);
        data.ApproveAccount(account.Id, Arg.Any<string?>())
            .Returns(Task.FromResult<string?>("Somebody else already decided this application."));
        var cut = Render(data);

        await cut.Find(".approve-banner button.btn-accent").ClickAsync(new MouseEventArgs());
        await cut.Find(".modal-foot button.btn-accent").ClickAsync(new MouseEventArgs());

        cut.Find("div.panel.panel-pad.note-dashed").TextContent
            .Should().Be("Somebody else already decided this application.");
        Toast.Received(1).Show("Could not approve the account");
        Toast.DidNotReceive().Show("Account approved");
        cut.FindAll(".approve-banner").Should().ContainSingle("the account is still Pending, since the approval was refused");
    }

    [Fact]
    public async Task RejectSubmitIsDisabledUntilAReasonIsGivenAndSendsItUnmodified()
    {
        const string reason = "Chamber of Commerce document does not match the company name given.";
        var account = NewAccount();
        var data = MockDataFor(account);
        data.RejectAccount(account.Id, Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        var cut = Render(data);

        await cut.Find(".approve-banner button.btn-danger-outline").ClickAsync(new MouseEventArgs());

        var submit = cut.Find(".modal-foot button.btn-danger-outline");
        submit.IsDisabled().Should().BeTrue("the reason box is still blank");

        await cut.Find(".modal-body textarea.input").ChangeAsync(new ChangeEventArgs { Value = reason });
        cut.Find(".modal-foot button.btn-danger-outline").IsDisabled().Should().BeFalse();

        await cut.Find(".modal-foot button.btn-danger-outline").ClickAsync(new MouseEventArgs());

        await data.Received(1).RejectAccount("AC-001", reason);
    }

    [Fact]
    public void ApprovalHistoryShowsOnlyAccountCreatedForAFreshApplicationAndInventsNothing()
    {
        var cut = Render(MockDataFor(NewAccount()));

        cut.FindAll(".hist").Should().ContainSingle();
        cut.Find(".hist .hist-body").TextContent.Should().Contain("Account created").And.Contain("2026-01-05");
        cut.FindAll(".note-dashed").Should().NotContain(e => e.TextContent.Contains("Reason given to the applicant"));
    }

    [Fact]
    public void ApprovalHistoryShowsTheRecordedApprovalAndNothingMore()
    {
        var account = NewAccount(status: "Approved", decidedBy: "jane@aclitrade.ie", decidedOn: "2026-02-01");
        var cut = Render(MockDataFor(account));

        var entries = cut.FindAll(".hist");
        entries.Should().HaveCount(2);
        entries[1].TextContent.Should().Contain("Application approved").And.Contain("jane@aclitrade.ie").And.Contain("2026-02-01");
    }

    [Fact]
    public void ApprovalHistoryShowsTheRejectionReasonVerbatim()
    {
        var account = NewAccount(
            status: "Rejected",
            decidedBy: "jane@aclitrade.ie",
            decidedOn: "2026-02-01",
            rejectionReason: "Missing VAT certificate");
        var cut = Render(MockDataFor(account));

        var entries = cut.FindAll(".hist");
        entries.Should().HaveCount(2);
        entries[1].TextContent.Should().Contain("Application rejected");
        cut.FindAll(".note-dashed").Should().Contain(e =>
            e.TextContent.Contains("Reason given to the applicant: Missing VAT certificate"));
    }

    private static AccountDocument NewDocument(int id, string name, string status = "Pending") => new()
    {
        Id = id,
        Name = name,
        Kind = "VatCertificate",
        ContentType = "application/pdf",
        SizeBytes = 4096,
        Uploaded = "2026-01-05",
        Status = status,
    };

    private static IElement DocumentBlock(IRenderedComponent<AccountDetail> cut, string fileName) =>
        cut.FindAll(".doc").Single(d => d.TextContent.Contains(fileName));

    private static IElement DocumentLink(IElement documentBlock, string text) =>
        documentBlock.QuerySelectorAll(".doc-view").Single(a => a.TextContent.Trim() == text);

    [Fact]
    public async Task AcceptingADocumentUpdatesOnlyThatDocument()
    {
        var documents = new[] { NewDocument(42, "vat-cert.pdf"), NewDocument(43, "chamber-cert.pdf") };
        var account = NewAccount();
        var data = MockDataFor(account, documents);
        data.SetDocumentStatus(43, "Accepted").Returns(Task.FromResult(true));
        var cut = Render(data);

        await DocumentLink(DocumentBlock(cut, "chamber-cert.pdf"), "Accept").ClickAsync(new MouseEventArgs());

        await data.Received(1).SetDocumentStatus(43, "Accepted");
        await data.DidNotReceive().SetDocumentStatus(42, "Accepted");
        Toast.Received(1).Show("Document marked accepted");
    }

    [Fact]
    public async Task RejectingADocumentSendsRejected()
    {
        var documents = new[] { NewDocument(42, "vat-cert.pdf") };
        var account = NewAccount();
        var data = MockDataFor(account, documents);
        data.SetDocumentStatus(42, "Rejected").Returns(Task.FromResult(true));
        var cut = Render(data);

        await DocumentLink(DocumentBlock(cut, "vat-cert.pdf"), "Reject").ClickAsync(new MouseEventArgs());

        await data.Received(1).SetDocumentStatus(42, "Rejected");
    }

    [Fact]
    public async Task DownloadingADocumentGoesThroughContentAndSaveFileRatherThanANavigatingHref()
    {
        var documents = new[] { NewDocument(42, "vat-cert.pdf") };
        var account = NewAccount();
        var data = MockDataFor(account, documents);
        data.GetDocumentContent(42).Returns(Task.FromResult<(string Name, string ContentType, string Base64)?>(
            ("vat-cert.pdf", "application/pdf", "QkFTRTY0")));
        var cut = Render(data);
        // bUnit's JSInterop throws for an unconfigured call rather than silently succeeding, so
        // the interop this test exists to prove happens has to be allowed through first. Setup
        // alone only stops the throw -- the invocation itself stays pending until something
        // completes it, so without SetVoidResult() the awaited ClickAsync below never returns:
        // not a slow test, an unbounded hang that blocks every test declared after this one and
        // is what the harness eventually reports as a crashed/aborted host.
        JSInterop.SetupVoid("smportal.saveFile", _ => true).SetVoidResult();

        await DocumentLink(DocumentBlock(cut, "vat-cert.pdf"), "Download").ClickAsync(new MouseEventArgs());

        await data.Received(1).GetDocumentContent(42);
        var invocation = JSInterop.VerifyInvoke("smportal.saveFile");
        invocation.Arguments.Should().Equal("vat-cert.pdf", "application/pdf", "QkFTRTY0");
    }

    [Fact]
    public async Task ShowsAToastAndSkipsSavingWhenTheDocumentIsGone()
    {
        var documents = new[] { NewDocument(42, "vat-cert.pdf") };
        var account = NewAccount();
        var data = MockDataFor(account, documents);
        data.GetDocumentContent(42).Returns(Task.FromResult<(string Name, string ContentType, string Base64)?>(null));
        var cut = Render(data);

        await DocumentLink(DocumentBlock(cut, "vat-cert.pdf"), "Download").ClickAsync(new MouseEventArgs());

        Toast.Received(1).Show("That document is no longer available");
        JSInterop.VerifyNotInvoke("smportal.saveFile");
    }
}
