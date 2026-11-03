using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Models;
using StockManager.Identity;
using Xunit;

namespace StockApi.Tests.Security;

// The staff login's own surface: the sign-in, the password change and the role changes. Each
// guard here is an absence — a hash not skipped, a password not checked, a role not granted —
// and nothing else fails when one goes.
public class StaffAccountGuardTests
{
    private readonly IPasswordHasher<IdentityUser> _hasher = Substitute.For<IPasswordHasher<IdentityUser>>();
    private readonly UserManager<IdentityUser> _users;
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly IUserData _userData = Substitute.For<IUserData>();

    public StaffAccountGuardTests()
    {
        _users = Substitute.For<UserManager<IdentityUser>>(
            Substitute.For<IUserStore<IdentityUser>>(), null!, _hasher, null!, null!, null!, null!, null!, null!);
        _signIn = Substitute.For<SignInManager<IdentityUser>>(
            _users, Substitute.For<IHttpContextAccessor>(), Substitute.For<IUserClaimsPrincipalFactory<IdentityUser>>(),
            null!, null!, null!, null!);
    }

    [Fact]
    public async Task AnUnknownStaffNameCostsAHashLikeAKnownOne()
    {
        // Returning straight after the lookup made the response time say which names exist.
        _users.FindByNameAsync(Arg.Any<string>()).Returns((IdentityUser?)null);
        var controller = new TokenController(_users, _signIn, Substitute.For<IConfiguration>());

        var result = await controller.Create("nobody@example.test", "guess", "password");

        result.Should().BeOfType<BadRequestObjectResult>();
        _hasher.ReceivedWithAnyArgs(1).HashPassword(default!, default!);
    }

    [Fact]
    public async Task ChangingAPasswordChecksTheCurrentOneAgainstLockoutFirst()
    {
        var me = new IdentityUser("staff@example.test");
        _users.FindByIdAsync(me.Id).Returns(me);
        _signIn.CheckPasswordSignInAsync(me, "wrong", lockoutOnFailure: true).Returns(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        var result = await Users(signedInAs: me).ChangeOwnPassword(new UserController.PasswordChangeModel("wrong", "New-Passw0rd!"));

        result.Should().BeOfType<BadRequestObjectResult>();
        await _users.DidNotReceiveWithAnyArgs().ChangePasswordAsync(default!, default!, default!);
    }

    [Fact]
    public async Task ACustomerLoginIsNotGivenAStaffRole()
    {
        var customer = new IdentityUser("test-store|buyer@example.test");
        _users.FindByIdAsync(customer.Id).Returns(customer);

        var result = await Users().AddRole(new UserRolePairModel { UserId = customer.Id, RoleName = "Admin" });

        result.Should().BeOfType<BadRequestObjectResult>();
        await _users.DidNotReceiveWithAnyArgs().AddToRoleAsync(default!, default!);
    }

    [Fact]
    public async Task OnlyAStaffRoleIsGranted()
    {
        var colleague = new IdentityUser("colleague@example.test");
        _users.FindByIdAsync(colleague.Id).Returns(colleague);

        var result = await Users().AddRole(new UserRolePairModel { UserId = colleague.Id, RoleName = "Owner" });

        result.Should().BeOfType<BadRequestObjectResult>();
        await _users.DidNotReceiveWithAnyArgs().AddToRoleAsync(default!, default!);
    }

    [Fact]
    public async Task TheLastAdminOfEveryStoreKeepsTheRole()
    {
        var onlyAdmin = new IdentityUser("boss@example.test");
        _users.FindByIdAsync(onlyAdmin.Id).Returns(onlyAdmin);
        _users.GetUsersInRoleAsync("Admin").Returns([onlyAdmin]);
        _userData.GetAllUsers().Returns([new UserModel { UserId = onlyAdmin.Id, AllSites = true }]);

        var result = await Users().RemoveRole(new UserRolePairModel { UserId = onlyAdmin.Id, RoleName = "Admin" });

        result.Should().BeOfType<ConflictObjectResult>();
        await _users.DidNotReceiveWithAnyArgs().RemoveFromRoleAsync(default!, default!);
    }

    [Fact]
    public async Task RemovingARoleEndsTheTokensThatCarryIt()
    {
        var colleague = new IdentityUser("colleague@example.test");
        _users.FindByIdAsync(colleague.Id).Returns(colleague);
        _users.RemoveFromRoleAsync(colleague, "Manager").Returns(IdentityResult.Success);

        var result = await Users().RemoveRole(new UserRolePairModel { UserId = colleague.Id, RoleName = "Manager" });

        result.Should().BeOfType<NoContentResult>();
        await _users.Received(1).UpdateSecurityStampAsync(colleague);
    }

    private UserController Users(IdentityUser? signedInAs = null)
    {
        var controller = new UserController(
            new ApplicationDbContext(new DbContextOptions<ApplicationDbContext>()),
            _users,
            _userData,
            NullLogger<UserController>.Instance,
            Substitute.For<IConfiguration>(),
            _signIn);

        var id = signedInAs?.Id ?? "admin-id";
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Bearer"))
            }
        };

        return controller;
    }
}
