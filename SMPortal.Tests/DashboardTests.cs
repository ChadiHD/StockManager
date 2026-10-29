using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// The dashboard greeted every admin as "Órla", a name nobody had entered. It is the staff
// profile's name now, or none.
public class DashboardTests : TestContext
{
    private readonly IAdminDataService _data = Substitute.For<IAdminDataService>();

    public DashboardTests()
    {
        _data.Accounts.Returns(Array.Empty<Account>());
        _data.Quotes.Returns(Array.Empty<Quote>());
        _data.Orders.Returns(Array.Empty<Order>());
        _data.Products.Returns(Array.Empty<Product>());
        _data.Activity.Returns(Array.Empty<ActivityItem>());
        Services.AddSingleton(_data);
    }

    [Fact]
    public void TheSignedInAdminIsWelcomedByTheirOwnName()
    {
        _data.SignedInName.Returns("Niamh Byrne");

        var cut = RenderComponent<Dashboard>();

        cut.Find(".dash-welcome").TextContent.Should().Be("Welcome back, Niamh Byrne");
    }

    [Fact]
    public void AnAdminWithNoNameOnFileIsWelcomedWithoutOne()
    {
        _data.SignedInName.Returns(string.Empty);

        var cut = RenderComponent<Dashboard>();

        cut.Find(".dash-welcome").TextContent.Should().Be("Welcome back");
        cut.Markup.Should().NotContain("Órla");
    }
}
