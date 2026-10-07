using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Security;
using Xunit;

namespace StockApi.Tests.Security;

/// <summary>
/// The policy on actions that are not about one store (T9). It must refuse an admin given only
/// some stores and must not touch the till's roles, which have no store at all.
/// </summary>
public class AllStoresTests
{
    private readonly IUserData _users = Substitute.For<IUserData>();

    private async Task<bool> Allows(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var requirement = new AllStores.Requirement();
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), resource: null);

        await new AllStores.Handler(_users).HandleAsync(context);

        return context.HasSucceeded;
    }

    private void Profile(string userId, bool allSites) =>
        _users.GetUserById(userId).Returns(new List<UserModel> { new() { UserId = userId, AllSites = allSites } });

    [Fact]
    public async Task AnAdminOfEveryStoreIsAllowed()
    {
        Profile("owner", allSites: true);

        (await Allows("owner", "Admin")).Should().BeTrue();
    }

    [Fact]
    public async Task AnAdminGivenSomeStoresIsRefused()
    {
        Profile("limited", allSites: false);

        (await Allows("limited", "Admin")).Should().BeFalse();
    }

    [Fact]
    public async Task AnAdminWithNoProfileIsRefused()
    {
        _users.GetUserById("ghost").Returns(new List<UserModel>());

        (await Allows("ghost", "Admin")).Should().BeFalse();
    }

    [Fact]
    public async Task TheTillsRolesAreLeftToTheActionsOwnRoles()
    {
        // A Manager reads the till's sales report with no store and no AllSites; the policy
        // must not take that away.
        Profile("manager", allSites: false);

        (await Allows("manager", "Manager")).Should().BeTrue();
        _users.DidNotReceiveWithAnyArgs().GetUserById(default!);
    }

    [Theory]
    [InlineData("owner", new[] { "owner", "limited" }, new[] { "owner" }, true)]
    [InlineData("owner", new[] { "owner", "second" }, new[] { "owner", "second" }, false)]
    [InlineData("limited", new[] { "owner", "limited" }, new[] { "owner" }, false)]
    // AllSites on somebody who is not an Admin manages nothing, so it does not count.
    [InlineData("owner", new[] { "owner" }, new[] { "owner", "a-manager" }, true)]
    public void TheLastAdminOfEveryStoreIsTheOnlyUserBothAdminAndEveryStore(
        string userId, string[] admins, string[] everyStore, bool isLast)
    {
        UserController.IsLastAdminOfEveryStore(userId, admins, everyStore).Should().Be(isLast);
    }
}
