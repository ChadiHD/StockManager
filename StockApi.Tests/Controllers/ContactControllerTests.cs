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
/// The people on a trading account, for the reviewer deciding whether to open it — see the
/// remarks on ContactController.
/// </summary>
public class ContactControllerTests
{
    private readonly IContactData _contacts = Substitute.For<IContactData>();
    private readonly IAccountData _accounts = Substitute.For<IAccountData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly ContactController _controller;

    public ContactControllerTests()
    {
        _site.SiteId.Returns(42);
        _controller = new ContactController(_contacts, _accounts, _site);
    }

    [Fact]
    public void GetByAccountReturnsNotFoundWhenTheAccountIsNotThisStores()
    {
        // The contact procedures join Account for the site predicate, so a bad id would return
        // an empty list on its own — the controller checks the account first specifically so
        // this reads as 404 rather than as "this customer has no contacts".
        _accounts.GetAccountById(5, 42).Returns((AccountModel?)null);

        var result = _controller.GetByAccount(5);

        result.Result.Should().BeOfType<NotFoundResult>();
        _contacts.DidNotReceive().GetByAccount(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void GetByAccountChecksOwnershipAndFetchesContactsUsingTheSiteContextsSiteId()
    {
        _accounts.GetAccountById(5, 42).Returns(new AccountModel { Id = 5 });
        _contacts.GetByAccount(5, 42).Returns(new List<ContactModel>
        {
            new() { Id = 1, AccountId = 5, FirstName = "Ada", LastName = "Byron", Email = "ada@example.com" }
        });

        var result = _controller.GetByAccount(5);

        // Both calls must use the site context's own id, not one taken from the request or
        // from the account row — an admin API has no other source of truth for which store it
        // is acting for.
        _accounts.Received(1).GetAccountById(5, 42);
        _contacts.Received(1).GetByAccount(5, 42);
        result.Value.Should().ContainSingle(item => item.Email == "ada@example.com");
    }

    [Fact]
    public void ContactListItemNeverExposesTheIdentityUserId()
    {
        // IdentityUserId identifies a credential in another database and no admin screen has
        // any use for it — see the remarks on ContactController.ContactListItem. Reflecting
        // over the projection catches a future change that widens it to the raw model.
        typeof(ContactController.ContactListItem).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain("IdentityUserId");
    }
}
