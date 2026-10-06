using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using Settings = SMPortal.Pages.Admin.Store.Settings;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// The store settings screen (T9): every dbo.Site value, which was set by hand before it existed.
public class StoreSettingsTests : TestContext
{
    private readonly IAdminDataService _data = Substitute.For<IAdminDataService>();

    public StoreSettingsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_data);
        Services.AddSingleton(Substitute.For<IToastService>());
    }

    private StoreSettingsView View(bool canChangeDomain = true, bool currencyLocked = false) => new()
    {
        Site = new StoreSettings
        {
            Id = 1, SiteKey = "acme", Name = "Acme", Domain = "shop.example.com", Country = "IE",
            CurrencyCode = "EUR", Locale = "en-IE", OrderMode = "Rfq", RegistrationFieldSet = "eu-b2b",
            PriceDisplay = "Public", TaxRuleSet = "eu-b2b", StandardTaxRatePct = 23,
            CurrencyLocked = currencyLocked
        },
        CanChangeDomain = canChangeDomain,
        OrderModes = ["Rfq"],
        RegistrationFieldSets = ["eu-b2b"],
        TaxRuleSets = ["eu-b2b"],
        PriceDisplays = ["Public", "Authenticated"]
    };

    [Fact]
    public void SavingSendsWhatTheAdminChose()
    {
        var view = View();
        _data.GetStoreSettings().Returns(view);
        _data.SaveStoreSettings(default!).ReturnsForAnyArgs((string?)null);
        var cut = RenderComponent<Settings>();

        cut.Find("#store-prices").Change("Authenticated");
        cut.Find("#store-legal-name").Change("Acme Trading Limited");
        cut.Find("form").Submit();

        _data.Received(1).SaveStoreSettings(Arg.Is<StoreSettings>(s =>
            s.PriceDisplay == "Authenticated" && s.LegalName == "Acme Trading Limited" && s.SiteKey == "acme"));
    }

    [Fact]
    public void ARefusalIsShownInTheAdminsWords()
    {
        _data.GetStoreSettings().Returns(View());
        _data.SaveStoreSettings(default!).ReturnsForAnyArgs("Another store already answers on that domain.");
        var cut = RenderComponent<Settings>();

        cut.Find("form").Submit();

        cut.Find(".store-settings-status").TextContent.Should().Contain("Another store already answers");
    }

    [Fact]
    public void OnlyAnAdminOfEveryStoreMayEditTheDomain()
    {
        _data.GetStoreSettings().Returns(View(canChangeDomain: false));

        var cut = RenderComponent<Settings>();

        cut.Find("#store-domain").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#store-key").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void AStoreThatHoldsPricesCannotChangeItsCurrency()
    {
        _data.GetStoreSettings().Returns(View(currencyLocked: true));

        var cut = RenderComponent<Settings>();

        cut.Find("#store-currency").HasAttribute("disabled").Should().BeTrue();
        cut.Markup.Should().Contain("every price it holds is in this currency");
    }

    [Fact]
    public void TheKeyedSettingsOfferOnlyWhatTheApiSays()
    {
        _data.GetStoreSettings().Returns(View());

        var cut = RenderComponent<Settings>();

        cut.FindAll("#store-prices option").Select(option => option.GetAttribute("value"))
            .Should().Equal("Public", "Authenticated");
    }
}
