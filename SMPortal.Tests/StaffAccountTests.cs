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
        _data.ManagesAllStores.Returns(true);
        _data.Sites.Returns(new List<SiteOption>
        {
            new(1, "ie-store", "Irish store", "IE", "EUR"),
            new(2, "uk-store", "UK store", "GB", "GBP")
        });
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
        _data.AddUser(default!, default!, default!, default!, default, default!).ReturnsForAnyArgs((string?)null);
        var cut = OpenAddUser();

        cut.Find(".modal form").Submit();

        _data.Received(1).AddUser("Ciara Walsh", "ciara@example.test", "Staff", "First-Passw0rd!",
            false, Arg.Is<IEnumerable<int>>(ids => !ids.Any()));
    }

    [Fact]
    public void ARefusedPasswordIsShownAndTheFormStaysOpen()
    {
        _data.AddUser(default!, default!, default!, default!, default, default!)
            .ReturnsForAnyArgs("Passwords must have at least one digit ('0'-'9').");
        var cut = OpenAddUser();

        cut.Find(".modal form").Submit();

        cut.Find(".invite-error").TextContent.Should().Contain("at least one digit");
        cut.FindAll(".modal").Should().NotBeEmpty();
    }

    [Fact]
    public void ANewColleagueIsGivenExactlyTheStoresTicked()
    {
        // T9: a colleague is given stores, not handed all of them because nobody unticked a box.
        _data.AddUser(default!, default!, default!, default!, default, default!).ReturnsForAnyArgs((string?)null);
        var cut = OpenAddUser();

        cut.Find(".modal input.store-one[value=uk-store]").Change(true);
        cut.Find(".modal form").Submit();

        _data.Received(1).AddUser("Ciara Walsh", "ciara@example.test", "Staff", "First-Passw0rd!",
            false, Arg.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 2 })));
    }

    [Fact]
    public void ChangingAccessIsByIdAndARefusalKeepsTheDialogOpen()
    {
        // The API will not take every store from the last admin who has it; the admin must read
        // why, not see the dialog close on a list that did not change.
        _data.Users.Returns(new List<User>
        {
            new() { Id = "id-owner", Name = "Only Owner", Email = "owner@example.test", Roles = "Admin", AllSites = true }
        });
        _data.UpdateUserAccess(default!, default!, default, default!)
            .ReturnsForAnyArgs("They are the only admin who can manage every store.");
        var cut = RenderComponent<Users>();

        cut.FindAll("button").First(button => button.TextContent.Contains("Manage")).Click();
        cut.Find(".modal input.store-all").Change(false);
        cut.FindAll(".modal button").First(button => button.TextContent.Trim() == "Save").Click();

        _data.Received(1).UpdateUserAccess("id-owner", Arg.Any<IEnumerable<string>>(), false, Arg.Any<IEnumerable<int>>());
        cut.Find(".manage-error").TextContent.Should().Contain("only admin");
        cut.FindAll(".modal").Should().NotBeEmpty();
    }

    [Fact]
    public void AnAdminGivenSomeStoresIsToldWhoManagesStaffAndOfferedNothing()
    {
        _data.ManagesAllStores.Returns(false);

        var cut = RenderComponent<Users>();

        cut.Find(".users-restricted").TextContent.Should().Contain("admin of every store");
        cut.FindAll("button").Should().NotContain(button => button.TextContent.Contains("Add user"));
    }

    [Fact]
    public void TheUsersPageSaysWhichStoresEachMemberOfStaffHas()
    {
        _data.Users.Returns(new List<User>
        {
            new() { Id = "a", Name = "Owner", Email = "owner@example.test", AllSites = true },
            new() { Id = "b", Name = "Uk Admin", Email = "uk@example.test", SiteIds = new List<int> { 2 } },
            new() { Id = "c", Name = "New Starter", Email = "new@example.test" }
        });

        var stores = RenderComponent<Users>().FindAll(".user-stores").Select(cell => cell.TextContent).ToList();

        stores.Should().Equal("All stores", "UK store", "None");
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
