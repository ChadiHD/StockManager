using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin.Quotes;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// Re-pricing is the one screen where an admin changes what a customer will be charged, and
// two of its rules are invisible from the markup. A line the operator typed and untyped must
// not be written at all; and a quote the customer decided while this page was open must be
// reported as decided rather than as a failure -- the distinction the API carries as a 409 and
// the reason QuotePricing has three cases instead of a bool.
public class QuoteDetailTests : TestContext
{
    private const string Reference = "QT-0041";

    public QuoteDetailTests()
    {
        Services.AddSingleton(Substitute.For<IToastService>());
    }

    private IToastService Toast => Services.GetRequiredService<IToastService>();

    private static Quote NewQuote(string status = "Requested") => new()
    {
        Id = Reference,
        Account = "Acme Trading",
        Currency = "EUR",
        Status = status,
        Created = "2026-09-01",
        Expires = "2026-09-30",
        Lines = 2,
        LineItems =
        [
            new QuoteLine { LineId = 11, Sku = "SKU-1", Name = "First", Qty = 3, List = 99.99m, Disc = 10m, Net = 90m },
            new QuoteLine { LineId = 12, Sku = "SKU-2", Name = "Second", Qty = 1, List = 50m, Disc = 0m, Net = 50m },
        ]
    };

    private static IAdminDataService MockDataFor(Quote quote)
    {
        var data = Substitute.For<IAdminDataService>();

        // Every list the page reads has to be configured: NSubstitute hands back null for an
        // unconfigured IReadOnlyList, which the real service never does.
        data.Accounts.Returns(new[] { new Account { Id = "AC-001", Company = "Acme Trading", Group = "Reseller" } });
        data.Products.Returns(Array.Empty<Product>());
        data.GetQuote(Reference).Returns(_ => quote);
        data.DiscountFor(Arg.Any<string>()).Returns(10);

        return data;
    }

    private IRenderedComponent<QuoteDetail> Render(IAdminDataService data)
    {
        Services.AddSingleton(data);

        return RenderComponent<QuoteDetail>(p => p.Add(x => x.Id, Reference));
    }

    [Fact]
    public void SaveDraftIsDisabledUntilALineActuallyChanges()
    {
        var cut = Render(MockDataFor(NewQuote()));

        cut.Find("button.quote-save").HasAttribute("disabled").Should().BeTrue();

        cut.FindAll("input.line-edit")[0].Change("5");

        cut.Find("button.quote-save").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void AValueTypedAndPutBackQueuesNothing()
    {
        var cut = Render(MockDataFor(NewQuote()));

        cut.FindAll("input.line-edit")[0].Change("5");
        cut.FindAll("input.line-edit")[0].Change("3");

        // Otherwise the operator saves a write that changes nothing, and the toast reports a
        // re-price that did not happen.
        cut.Find("button.quote-save").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task SavingWritesOnlyTheLinesThatChanged()
    {
        var data = MockDataFor(NewQuote());
        data.UpdateQuoteLine(Reference, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>())
            .Returns(Task.FromResult(true));

        var cut = Render(data);

        // The quantity on the first line, and nothing on the second.
        cut.FindAll("input.line-edit")[0].Change("5");

        await cut.Find("button.quote-save").ClickAsync(new MouseEventArgs());

        await data.Received(1).UpdateQuoteLine(Reference, 11, 5, 10m);
        await data.DidNotReceive().UpdateQuoteLine(Reference, 12, Arg.Any<int>(), Arg.Any<decimal>());
    }

    [Fact]
    public async Task ADiscountKeepsItsSecondDecimalPlace()
    {
        var data = MockDataFor(NewQuote());
        data.UpdateQuoteLine(Reference, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>())
            .Returns(Task.FromResult(true));

        var cut = Render(data);

        // QuoteLine.DiscountPct is DECIMAL(5, 2) because a price held up by the margin floor
        // has a fractional effective discount. Rounding to a whole number here would re-price
        // a line the operator was only looking at.
        cut.FindAll("input.line-edit")[1].Change("12.75");

        await cut.Find("button.quote-save").ClickAsync(new MouseEventArgs());

        await data.Received(1).UpdateQuoteLine(Reference, 11, 3, 12.75m);
    }

    [Fact]
    public async Task SendingIsRefusedWhileEditsAreStillUnsaved()
    {
        var data = MockDataFor(NewQuote());
        var cut = Render(data);

        cut.FindAll("input.line-edit")[0].Change("5");

        await cut.Find("button.quote-send").ClickAsync(new MouseEventArgs());

        // The customer would otherwise be able to accept a price the operator has on screen
        // but has not written.
        await data.DidNotReceive().SendQuoteToCustomer(Arg.Any<string>());
        Toast.Received(1).Show(Arg.Is<string>(m => m.Contains("Save the draft first")));
    }

    [Fact]
    public async Task SendingReportsThatTheCustomerDecidedFirstRatherThanAFailure()
    {
        var data = MockDataFor(NewQuote());
        data.SendQuoteToCustomer(Reference).Returns(Task.FromResult(QuotePricing.AlreadyDecided));

        var cut = Render(data);

        await cut.Find("button.quote-send").ClickAsync(new MouseEventArgs());

        // An operator told "that failed" about a customer accepting their own quote learns to
        // discount the message that matters.
        Toast.Received(1).Show(Arg.Is<string>(m => m.Contains("already been decided")));
        Toast.DidNotReceive().Show(Arg.Is<string>(m => m.Contains("Could not send")));
    }

    [Fact]
    public async Task SendingASuccessfulQuoteSaysTheCustomerCanActOnIt()
    {
        var data = MockDataFor(NewQuote());
        data.SendQuoteToCustomer(Reference).Returns(Task.FromResult(QuotePricing.Sent));

        var cut = Render(data);

        await cut.Find("button.quote-send").ClickAsync(new MouseEventArgs());

        await data.Received(1).SendQuoteToCustomer(Reference);
        Toast.Received(1).Show(Arg.Is<string>(m => m.Contains("Acme Trading")));
    }

    [Fact]
    public void AnAcceptedQuotesLinesCannotBeEdited()
    {
        var cut = Render(MockDataFor(NewQuote(status: "Accepted")));

        // The procedure refuses it too. This is the control being withdrawn rather than
        // offered and then refused, which is the same reasoning as the removal button.
        cut.FindAll("input.line-edit").Should().BeEmpty();
        cut.Find("button.quote-save").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TheStepperFollowsTheStatusTheQuoteCurrentlyHas()
    {
        var cut = Render(MockDataFor(NewQuote(status: "Priced")));

        // Recomputed on every read rather than once on load, because "Send to customer" moves
        // the status from this page: left where it was, the stepper would still read
        // Requested beside a badge reading Priced.
        cut.FindAll(".step-dot--active").Should().HaveCount(1);
        cut.Find(".step-dot--active").ParentElement!.TextContent.Should().Contain("Priced");
    }
}
