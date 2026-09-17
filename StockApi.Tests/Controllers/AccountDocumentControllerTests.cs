using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using StockManager.Documents;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// The reviewer's side of customer document upload — see the remarks on
/// AccountDocumentController. StoredName is the key into the document store, so the guard that
/// GetByAccount never carries it is a security property, not a display choice.
/// </summary>
public class AccountDocumentControllerTests
{
    private readonly IAccountDocumentData _documents = Substitute.For<IAccountDocumentData>();
    private readonly IAccountData _accounts = Substitute.For<IAccountData>();
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly AccountDocumentController _controller;

    public AccountDocumentControllerTests()
    {
        _site.SiteId.Returns(42);
        _site.Site.Returns(new SiteModel { Id = 42, SiteKey = "test-store" });

        _controller = new AccountDocumentController(_documents, _accounts, _store, _site);
    }

    private static AccountDocumentModel Document() => new()
    {
        Id = 11,
        AccountId = 5,
        Kind = "VatCertificate",
        StoredName = "abc123.pdf",
        OriginalName = "vat-certificate.pdf",
        ContentType = "application/pdf",
        SizeBytes = 1024,
        Status = "Pending"
    };

    [Fact]
    public void DocumentListItemNeverExposesStoredName()
    {
        // StoredName is the key into the document store — see the remarks on
        // AccountDocumentModel and on GetByAccount below. Reflecting over the projection's own
        // shape is what catches a future change that returns the model instead of this DTO.
        typeof(AccountDocumentController.DocumentListItem).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain("StoredName");
    }

    [Fact]
    public void GetByAccountReturnsNotFoundWhenTheAccountIsNotThisStores()
    {
        _accounts.GetAccountById(5, 42).Returns((AccountModel?)null);

        var result = _controller.GetByAccount(5);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetByAccountProjectsTheRemainingFieldsFromTheModel()
    {
        _accounts.GetAccountById(5, 42).Returns(new AccountModel { Id = 5 });
        _documents.GetByAccount(5, 42).Returns(new List<AccountDocumentModel> { Document() });

        var result = _controller.GetByAccount(5);

        result.Value.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 11,
            AccountId = 5,
            Kind = "VatCertificate",
            OriginalName = "vat-certificate.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024L,
            Status = "Pending"
        });
    }

    [Fact]
    public async Task GetContentReturnsNotFoundWhenTheDocumentDoesNotResolve()
    {
        // 404, not 403: the query already carries the site predicate, so a document belonging
        // to another store resolves to null exactly like one that never existed — see the
        // remarks on GetById and GetContent.
        _documents.GetById(11, 42).Returns((AccountDocumentModel?)null);

        var result = await _controller.GetContent(11, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetContentReturnsNotFoundWhenTheStoreHasNoFileForTheDocument()
    {
        _documents.GetById(11, 42).Returns(Document());
        _store.OpenAsync("test-store", "abc123.pdf", Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await _controller.GetContent(11, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Approved")]
    [InlineData("pending")]
    public void SetStatusReturnsBadRequestForAnInvalidStatus(string status)
    {
        var result = _controller.SetStatus(11, new AccountDocumentController.DocumentStatusChange(status));

        result.Should().BeOfType<BadRequestObjectResult>();
        _documents.DidNotReceive().SetStatus(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public void SetStatusReturnsNotFoundWhenTheDocumentDoesNotResolve()
    {
        _documents.GetById(11, 42).Returns((AccountDocumentModel?)null);

        var result = _controller.SetStatus(11, new AccountDocumentController.DocumentStatusChange("Accepted"));

        result.Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Accepted")]
    [InlineData("Rejected")]
    public void SetStatusUpdatesAndReturnsNoContentForAnAllowedStatus(string status)
    {
        _documents.GetById(11, 42).Returns(Document());

        var result = _controller.SetStatus(11, new AccountDocumentController.DocumentStatusChange(status));

        result.Should().BeOfType<NoContentResult>();
        _documents.Received(1).SetStatus(11, status, 42);
    }
}
