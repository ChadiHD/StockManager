using System.Data.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Security;
using Xunit;
using Step = StockApi.Security.AdminBootstrap.Step;

namespace StockApi.Tests.Security;

// What a deployment does at startup about its first admin. The shell around this is
// UserManager calls; the decision is the part with a wrong answer in it.
public class AdminBootstrapTests
{
    [Fact]
    public void OnceAnAdminExistsTheConfiguredValuesDoNothing()
    {
        // Left configured after the first deploy, they must not mint a second admin or reset
        // the first one's password -- whoever can read the deployment's settings is not
        // thereby an admin.
        AdminBootstrap.Decide(anyAdmin: true, "boss@example.test", "Secret-1!", loginExists: true)
            .Step.Should().Be(Step.IgnoreConfigured);

        AdminBootstrap.Decide(anyAdmin: true, email: null, password: null, loginExists: false)
            .Step.Should().Be(Step.Nothing);
    }

    [Fact]
    public void AnAdminMadeBeforeTheSchemaIsFinished()
    {
        // A first deploy starts StockApi before SMDatabase has its schema: the login and its role
        // were made, the profile was not, and the portal refused a correct password for ever.
        AdminBootstrap.Decide(anyAdmin: true, "boss@example.test", "Secret-1!", loginExists: true, unfinished: true)
            .Step.Should().Be(Step.Finish);
    }

    [Fact]
    public void WithNoAdminAndNothingConfiguredItOnlyWarns()
    {
        AdminBootstrap.Decide(anyAdmin: false, email: "", password: null, loginExists: false)
            .Step.Should().Be(Step.WarnNoAdmin);
    }

    [Fact]
    public void AnExistingLoginIsPromotedAndKeepsItsPassword()
    {
        AdminBootstrap.Decide(anyAdmin: false, "staff@example.test", password: null, loginExists: true)
            .Step.Should().Be(Step.Promote);
    }

    [Fact]
    public void ANewAdminNeedsAPassword()
    {
        AdminBootstrap.Decide(anyAdmin: false, "boss@example.test", "Secret-1!", loginExists: false)
            .Step.Should().Be(Step.Create);

        var (step, reason) = AdminBootstrap.Decide(anyAdmin: false, "boss@example.test", password: "", loginExists: false);
        step.Should().Be(Step.Refuse);
        reason.Should().Contain("Admin:BootstrapPassword");
    }

    [Fact]
    public void ACustomerLoginNameIsNeverMadeAnAdmin()
    {
        // /token refuses a site-qualified name, so an admin by that name could never sign in;
        // and a customer login gaining Admin is the one outcome this must not have.
        AdminBootstrap.Decide(anyAdmin: false, "aclitrade|buyer@example.test", "Secret-1!", loginExists: true)
            .Step.Should().Be(Step.Refuse);
    }

    // The shell, for the one path where it decides something: finishing an admin.

    private static readonly IdentityUser Boss = new() { Id = "boss-id", UserName = "boss@example.test" };

    [Fact]
    public async Task AnAdminWithNoProfileGetsOneWithEveryStore()
    {
        var profiles = Substitute.For<IUserData>();
        profiles.GetUserById(Boss.Id).Returns([]);

        await RunAsync(profiles);

        profiles.Received().CreateUser(Arg.Is<UserModel>(user => user.UserId == Boss.Id));
        profiles.Received().SetSiteAccess(Boss.Id, true, Arg.Any<IEnumerable<int>>());
    }

    [Fact]
    public async Task ARestartDoesNotGiveAFinishedAdminTheirStoresBack()
    {
        // Somebody may have since taken stores away from the first admin. While the values stay
        // configured every start passes through here, and must not undo that.
        var profiles = Substitute.For<IUserData>();
        profiles.GetUserById(Boss.Id).Returns([new UserModel { UserId = Boss.Id }]);

        await RunAsync(profiles);

        profiles.DidNotReceiveWithAnyArgs().CreateUser(default!);
        profiles.DidNotReceiveWithAnyArgs().SetSiteAccess(default!, default, default!);
    }

    [Fact]
    public async Task AnSMDatabaseItCannotReadStopsNeitherTheHostNorAnybodysStores()
    {
        // A first deploy's first start has no schema: thrown, this kept the host down until a
        // restart, and that restart found an admin and stopped. And a read that fails and then
        // works must not be taken for a missing profile, which would hand back every store.
        var profiles = Substitute.For<IUserData>();
        profiles.GetUserById(Boss.Id).Returns(
            _ => throw new NoSchemaException(), _ => [new UserModel { UserId = Boss.Id }]);

        var run = () => RunAsync(profiles);

        await run.Should().NotThrowAsync();
        profiles.DidNotReceiveWithAnyArgs().SetSiteAccess(default!, default, default!);
    }

    private static async Task RunAsync(IUserData profiles)
    {
        var roles = Substitute.For<RoleManager<IdentityRole>>(
            Substitute.For<IRoleStore<IdentityRole>>(), null!, null!, null!, null!);
        roles.RoleExistsAsync(Arg.Any<string>()).Returns(true);

        var users = Substitute.For<UserManager<IdentityUser>>(
            Substitute.For<IUserStore<IdentityUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.GetUsersInRoleAsync(AdminBootstrap.AdminRole).Returns([Boss]);
        users.FindByNameAsync(Boss.UserName!).Returns(Boss);
        users.IsInRoleAsync(Boss, AdminBootstrap.AdminRole).Returns(true);

        var services = new ServiceCollection()
            .AddSingleton(roles)
            .AddSingleton(users)
            .AddSingleton(profiles)
            .BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:BootstrapEmail"] = Boss.UserName,
                ["Admin:BootstrapPassword"] = "Secret-1!"
            })
            .Build();

        await AdminBootstrap.EnsureAdminAsync(services, config, NullLogger.Instance);
    }

    private sealed class NoSchemaException : DbException;
}
