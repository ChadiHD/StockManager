using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>Billing and delivery addresses on a trading account — see the remarks on AddressController.</summary>
public class AddressControllerTests
{
    private readonly IAddressData _addresses = Substitute.For<IAddressData>();
    private readonly IAccountData _accounts = Substitute.For<IAccountData>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly AddressController _controller;

    public AddressControllerTests()
    {
        _site.SiteId.Returns(42);
        _controller = new AddressController(_addresses, _accounts, _site);
    }

    [Fact]
    public void GetByAccountReturnsNotFoundWhenTheAccountIsNotThisStores()
    {
        _accounts.GetAccountById(5, 42).Returns((AccountModel?)null);

        var result = _controller.GetByAccount(5);

        result.Result.Should().BeOfType<NotFoundResult>();
        _addresses.DidNotReceive().GetByAccount(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void GetByAccountChecksOwnershipAndFetchesAddressesUsingTheSiteContextsSiteId()
    {
        _accounts.GetAccountById(5, 42).Returns(new AccountModel { Id = 5 });
        var addresses = new List<AddressModel> { new() { Id = 1, AccountId = 5, Kind = "Billing" } };
        _addresses.GetByAccount(5, 42).Returns(addresses);

        var result = _controller.GetByAccount(5);

        _accounts.Received(1).GetAccountById(5, 42);
        _addresses.Received(1).GetByAccount(5, 42);
        result.Value.Should().BeSameAs(addresses);
    }
}
