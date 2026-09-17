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

// Accounts.razor: the status pills filter the in-memory snapshot, the search box only narrows
// it on submit (SearchBoxTests covers that submit-only contract on its own), and a filter
// change resets paging to page one -- the alternative is a filtered-down list stuck on a page
// number that no longer exists, rendering nothing.
public class AccountsTests : TestContext
{
    private static Account NewAccount(string id, string company, string status) => new()
    {
        Id = id,
        Company = company,
        Status = status,
        Country = "Ireland",
        Currency = "EUR",
        Group = "Reseller",
        Payment = "Card",
    };

    private IRenderedComponent<Accounts> Render(IReadOnlyList<Account> accounts)
    {
        Services.AddSingleton(Substitute.For<IToastService>());
        var data = Substitute.For<IAdminDataService>();
        data.Accounts.Returns(accounts);
        data.Groups.Returns(Array.Empty<Group>());
        Services.AddSingleton(data);
        return RenderComponent<Accounts>();
    }

    private static IElement Pill(IRenderedComponent<Accounts> cut, string label) =>
        cut.FindAll(".pill").Single(b => b.TextContent.Trim() == label);

    private static IReadOnlyList<string> ListedCompanies(IRenderedComponent<Accounts> cut) =>
        cut.FindAll("table.tbl tbody tr .cell-primary").Select(e => e.TextContent).ToList();

    [Fact]
    public async Task FilterPillsShowOnlyAccountsInThatStatus()
    {
        var accounts = new[]
        {
            NewAccount("AC-1", "Acme Trading", "Approved"),
            NewAccount("AC-2", "Beacon Supplies", "Pending"),
            NewAccount("AC-3", "Cobalt Systems", "Suspended"),
            NewAccount("AC-4", "Delta Traders", "Pending"),
        };
        var cut = Render(accounts);

        ListedCompanies(cut).Should().Equal("Acme Trading", "Beacon Supplies", "Cobalt Systems", "Delta Traders");

        await Pill(cut, "Pending").ClickAsync(new MouseEventArgs());
        ListedCompanies(cut).Should().Equal("Beacon Supplies", "Delta Traders");

        await Pill(cut, "Suspended").ClickAsync(new MouseEventArgs());
        ListedCompanies(cut).Should().Equal("Cobalt Systems");

        await Pill(cut, "All").ClickAsync(new MouseEventArgs());
        ListedCompanies(cut).Should().Equal("Acme Trading", "Beacon Supplies", "Cobalt Systems", "Delta Traders");
    }

    [Fact]
    public async Task ChangingTheFilterAfterPagingForwardReturnsToPageOne()
    {
        // 25 Approved plus 12 Pending: the default page size is 25, so "All" fills page one
        // with every Approved row and puts every Pending row on page two.
        var accounts = new List<Account>();
        for (var i = 1; i <= 25; i++)
        {
            accounts.Add(NewAccount($"AC-A{i:00}", $"Approved {i:00}", "Approved"));
        }
        for (var i = 1; i <= 12; i++)
        {
            accounts.Add(NewAccount($"AC-P{i:00}", $"Pending {i:00}", "Pending"));
        }

        var cut = Render(accounts);
        ListedCompanies(cut).Should().HaveCount(25);

        await cut.Find(".pager-btn[aria-label='Next page']").ClickAsync(new MouseEventArgs());
        ListedCompanies(cut).Should().HaveCount(12, "page two of All is the twelve Pending accounts");

        await Pill(cut, "Pending").ClickAsync(new MouseEventArgs());

        // Had the page not been reset, Skip(25) over a 12-row Pending-only list would render
        // nothing at all rather than the first page of the new filter.
        ListedCompanies(cut).Should().HaveCount(12);
    }

    [Fact]
    public async Task SearchOnlyNarrowsTheListOnSubmitNotOnEveryKeystroke()
    {
        var accounts = new[]
        {
            NewAccount("AC-1", "Acme Trading", "Approved"),
            NewAccount("AC-2", "Zenith Corp", "Approved"),
            NewAccount("AC-3", "Acme Distribution", "Approved"),
        };
        var cut = Render(accounts);

        await cut.Find(".searchbox input[type=search]").InputAsync(new ChangeEventArgs { Value = "Acme" });
        ListedCompanies(cut).Should().HaveCount(3, "typing alone must not filter the table");

        await cut.Find(".searchbox input[type=search]").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        ListedCompanies(cut).Should().Equal("Acme Trading", "Acme Distribution");
        cut.Find(".toolbar-count strong").TextContent.Should().Be("2");
    }
}
