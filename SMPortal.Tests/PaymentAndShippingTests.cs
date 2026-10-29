using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin.Store;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

/// <summary>A store's payment keys and delivery rates, on the admin screens (T13).</summary>
public class PaymentAndShippingTests : TestContext
{
    private readonly IAdminDataService _data = Substitute.For<IAdminDataService>();

    public PaymentAndShippingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_data);
        Services.AddSingleton(Substitute.For<IToastService>());
    }

    [Fact]
    public void KeysAreSentOnTheirOwnAndForgottenOnceSaved()
    {
        _data.GetStoreSettings().Returns(new StoreSettingsView
        {
            Site = new StoreSettings { SiteKey = "shop", Domain = "shop.example.com", OrderMode = "DirectCheckout" },
            OrderModes = ["Rfq", "DirectCheckout"], RegistrationFieldSets = ["b2c"], TaxRuleSets = ["flat"],
            PriceDisplays = ["Public"]
        });
        _data.SavePaymentKeys(default!, default!).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<Settings>();

        cut.Find(".store-payment-status").TextContent.Should().Contain("No keys");
        cut.Find("#store-payment-secret").Change("sk_test_abc");
        cut.Find("#store-payment-webhook").Change("whsec_def");
        cut.Find(".store-payment-save").Click();

        _data.Received(1).SavePaymentKeys("sk_test_abc", "whsec_def");
        _data.DidNotReceiveWithAnyArgs().SaveStoreSettings(default!);

        // Write-only: the page holds nothing once they are sent, and says they are set.
        cut.Find("#store-payment-secret").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Find(".store-payment-status").TextContent.Should().Contain("Keys are set");
    }

    [Fact]
    public void ARateIsAddedAndSwitchedOffRatherThanDeleted()
    {
        IReadOnlyList<ShippingRate> rates = [new ShippingRate { Id = 3, Name = "Brazil", Countries = "BR", Price = 15m, IsActive = true }];
        _data.GetShippingRates().Returns(Task.FromResult(rates));
        _data.SaveShippingRate(default!).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<Shipping>();

        cut.Find(".shipping-table").TextContent.Should().Contain("Brazil").And.Contain("BR");

        cut.Find("#shipping-name").Change("World");
        cut.Find("#shipping-price").Change("40");
        cut.Find(".shipping-save").Click();
        _data.Received(1).SaveShippingRate(Arg.Is<ShippingRate>(rate => rate.Id == 0 && rate.Name == "World" && rate.Price == 40m));

        cut.Find(".shipping-toggle").Click();
        _data.Received(1).SaveShippingRate(Arg.Is<ShippingRate>(rate => rate.Id == 3 && !rate.IsActive));
    }
}
