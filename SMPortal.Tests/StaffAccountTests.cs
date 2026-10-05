using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin.Users;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// A staff member added from the portal used to be unable ever to sign in: AddUser sent a random
// password that was never shown or stored, and staff have no reset path. Now the admin chooses
// the first password and the new user changes it themselves.
public class StaffAccountTests : TestContext
{
    private readonly IAdminDataService _data = Substitute.For<IAdminDataService>();

    public StaffAccountTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _data.Users.Returns(new List<User>());
        Services.AddSingleton(_data);
        Services.AddSingleton(Substitute.For<IToastService>());
    }

    private IRenderedComponent<Users> OpenAddUser()
    {
        var cut = RenderComponent<Users>();
        cut.FindAll("button").First(button => button.TextContent.Contains("Add user")).Click();
        cut.FindAll(".modal input")[0].Change("Ciara Walsh");
        cut.Find(".modal input[type=email]").Change("ciara@example.test");
        cut.Find(".modal input[type=password]").Change("First-Passw0rd!");
        return cut;
    }

    [Fact]
    public void AddingAUserSendsThePasswordTheAdminChose()
    {
        _data.AddUser(default!, default!, default!, default!).ReturnsForAnyArgs((string?)null);
        var cut = OpenAddUser();

        cut.Find(".modal form").Submit();

        _data.Received(1).AddUser("Ciara Walsh", "ciara@example.test", "Staff", "First-Passw0rd!");
    }

    [Fact]
    public void ARefusedPasswordIsShownAndTheFormStaysOpen()
    {
        _data.AddUser(default!, default!, default!, default!)
            .ReturnsForAnyArgs("Passwords must have at least one digit ('0'-'9').");
        var cut = OpenAddUser();

        cut.Find(".modal form").Submit();

        cut.Find(".invite-error").TextContent.Should().Contain("at least one digit");
        cut.FindAll(".modal").Should().NotBeEmpty();
    }

    [Fact]
    public void NothingOnTheUsersPageIsMadeUp()
    {
        // It rendered "Active now" and "2 h ago" from a dictionary of invented addresses.
        _data.Users.Returns(new List<User> { new() { Name = "Real Person", Email = "real@example.test" } });

        var cut = RenderComponent<Users>();

        cut.Markup.Should().NotContain("Last active").And.NotContain("Active now");
    }

    [Fact]
    public void ChangingAPasswordSendsCurrentAndNew()
    {
        _data.ChangePassword(default!, default!).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<ChangePassword>();

        cut.Find("#current-password").Change("First-Passw0rd!");
        cut.Find("#new-password").Change("Second-Passw0rd!");
        cut.Find("#confirm-password").Change("Second-Passw0rd!");
        cut.Find("form").Submit();

        _data.Received(1).ChangePassword("First-Passw0rd!", "Second-Passw0rd!");
    }

    [Fact]
    public void TwoDifferentNewPasswordsCannotBeSaved()
    {
        var cut = RenderComponent<ChangePassword>();

        cut.Find("#current-password").Change("First-Passw0rd!");
        cut.Find("#new-password").Change("Second-Passw0rd!");
        cut.Find("#confirm-password").Change("Typo-Passw0rd!");

        cut.Find("button.password-save").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".password-status").TextContent.Should().Contain("differ");
    }

    [Fact]
    public void AWrongCurrentPasswordIsShown()
    {
        _data.ChangePassword(default!, default!).ReturnsForAnyArgs("Incorrect password.");
        var cut = RenderComponent<ChangePassword>();

        cut.Find("#current-password").Change("Wrong-Passw0rd!");
        cut.Find("#new-password").Change("Second-Passw0rd!");
        cut.Find("#confirm-password").Change("Second-Passw0rd!");
        cut.Find("form").Submit();

        cut.Find(".password-status").TextContent.Should().Contain("Incorrect password");
    }
}
