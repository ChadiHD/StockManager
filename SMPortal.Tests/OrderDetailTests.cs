using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin.Orders;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

/// <summary>
/// An order's delivery address and its cancellation, on the admin's order screen (T12).
/// </summary>
/// <remarks>
/// Until T12 the screen printed one fixed address in Cork on every order, which the rule against
/// the portal inventing data forbids. It now prints the order's own, or says there is none.
/// </remarks>
public class OrderDetailTests : TestContext
{
    private const string Reference = "SO-0042";

    private readonly IAdminDataService _data = Substitute.For<IAdminDataService>();
    private readonly IToastService _toast = Substitute.For<IToastService>();

    public OrderDetailTests()
    {
        _data.GetOrder(Reference).Returns(new Order
        {
            Id = Reference, Account = "Ana Silva", Currency = "BRL", Status = "Awaiting payment", LineItems = []
        });

        Services.AddSingleton(_toast);
        Services.AddSingleton(_data);
    }

    [Fact]
    public void TheOrderShipsToTheAddressItsCheckoutGave()
    {
        _data.GetDeliveryAddress(Reference).Returns(Task.FromResult(new[] { "Ana Silva", "Rua A, 10", "São Paulo", "BR" }));

        var cut = RenderComponent<OrderDetail>(p => p.Add(x => x.Id, Reference));

        cut.Find(".ship").TextContent.Should().Contain("Rua A, 10").And.NotContain("Eastgate");
    }

    [Fact]
    public void AnOrderWithNoAddressSaysSoRatherThanInventingOne()
    {
        _data.GetDeliveryAddress(Reference).Returns(Task.FromResult(Array.Empty<string>()));

        var cut = RenderComponent<OrderDetail>(p => p.Add(x => x.Id, Reference));

        cut.Find(".ship").TextContent.Should().Contain("No delivery address");
    }

    [Fact]
    public void ARefusedStatusChangeSaysWhy()
    {
        _data.GetDeliveryAddress(Reference).Returns(Task.FromResult(Array.Empty<string>()));
        _data.SetOrderStatus(Reference, "Fulfilled")
            .Returns(Task.FromResult<string?>("A cancelled order placed from the cart cannot be reopened."));

        var cut = RenderComponent<OrderDetail>(p => p.Add(x => x.Id, Reference));
        cut.Find(".order-cancel").Click();
        _data.Received(1).SetOrderStatus(Reference, "Cancelled");

        cut.FindAll(".head-actions .btn-primary").Single().Click();

        _toast.Received(1).Show("A cancelled order placed from the cart cannot be reopened.");
    }

    private void FiscalDocument(string status, string? number = null, string? url = null, string? error = null)
    {
        _data.GetDeliveryAddress(Reference).Returns(Task.FromResult(Array.Empty<string>()));
        _data.GetOrder(Reference).Returns(new Order
        {
            Id = Reference, Account = "Ana Silva", Currency = "BRL", Status = "Paid", LineItems = [],
            FiscalStatus = status, FiscalNumber = number, FiscalUrl = url, FiscalError = error
        });
    }

    [Fact]
    public void AnOrderThatOwesNoFiscalDocumentShowsNoPanel()
    {
        _data.GetDeliveryAddress(Reference).Returns(Task.FromResult(Array.Empty<string>()));

        var cut = RenderComponent<OrderDetail>(p => p.Add(x => x.Id, Reference));

        cut.FindAll(".order-fiscal").Should().BeEmpty();
    }

    [Fact]
    public void AnIssuedDocumentShowsItsNumberAndOnlyAnHttpsLink()
    {
        FiscalDocument("Issued", "35261012345", "javascript:alert(1)");

        var cut = RenderComponent<OrderDetail>(p => p.Add(x => x.Id, Reference));

        cut.Find(".order-fiscal-status").TextContent.Should().Be("Issued");
        cut.Find(".order-fiscal-number").TextContent.Should().Be("35261012345");
        cut.Find(".order-fiscal").InnerHtml.Should().NotContain("javascript:", "the address is the issuer's answer");
        cut.FindAll(".order-fiscal-retry").Should().BeEmpty();
    }

    [Fact]
    public void AFailedDocumentSaysWhyAndCanBeSentAgain()
    {
        FiscalDocument("DeadLettered", error: "Rejeição 539");
        _data.RetryFiscalDocument(Reference).Returns(Task.FromResult<string?>(null));

        var cut = RenderComponent<OrderDetail>(p => p.Add(x => x.Id, Reference));

        cut.Find(".order-fiscal-status").TextContent.Should().Be("Failed");
        cut.Find(".order-fiscal-error").TextContent.Should().Contain("Rejeição 539");
        cut.Find(".order-fiscal-retry").Click();

        _data.Received(1).RetryFiscalDocument(Reference);
        _toast.Received(1).Show($"The fiscal document for {Reference} will be issued again");
    }
}
