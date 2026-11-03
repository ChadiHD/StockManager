using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Registration;
using SMStore.Sites;
using SMStore.Tests.TestSupport;
using Xunit;

namespace SMStore.Tests;

public class RegistrationServiceTests
{
    [Fact]
    public async Task AWeakPasswordGetsTheSameAnswerWhetherOrNotTheAddressIsRegistered()
    {
        // The password rules used to run only inside CreateAsync, which a known address never
        // reaches: a password of "a" drew Identity's complaints for a new address and
        // "Application received" for a registered one, which is the oracle the neutral answer
        // exists to deny.
        var unknown = await RegisterAsync(addressIsRegistered: false);
        var known = await RegisterAsync(addressIsRegistered: true);

        known.Accepted.Should().Be(unknown.Accepted).And.BeFalse();
        known.Errors.Select(error => error.Message).Should().Equal(unknown.Errors.Select(error => error.Message));
    }

    private static async Task<RegistrationOutcome> RegisterAsync(bool addressIsRegistered)
    {
        var site = new SiteModel { Id = 1, SiteKey = "test", Name = "Test store", Country = "IE", RegistrationFieldSet = "fake" };
        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(site);

        var users = Substitute.For<UserManager<IdentityUser>>(
            Substitute.For<IUserStore<IdentityUser>>(), null!, null!, null!,
            new IPasswordValidator<IdentityUser>[] { new PasswordValidator<IdentityUser>() },
            null!, null!, null!, null!);
        users.FindByNameAsync(Arg.Any<string>())
            .Returns(addressIsRegistered ? new IdentityUser("test|ada@example.com") : null);
        // What Identity says about "a", for the path that reaches it.
        users.CreateAsync(Arg.Any<IdentityUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityErrorDescriber().PasswordTooShort(6)));

        var service = new RegistrationService(
            users,
            Substitute.For<IAccountRegistrationData>(),
            new RegistrationFieldSetProvider(siteContext, [new FakeRegistrationFieldSet("fake", new Dictionary<RegistrationField, RegistrationRequirement>())]),
            siteContext,
            NullLogger<RegistrationService>.Instance);

        return await service.RegisterAsync(new RegistrationSubmission
        {
            Email = "ada@example.com",
            Password = "a",
            ConfirmPassword = "a"
        });
    }
}
