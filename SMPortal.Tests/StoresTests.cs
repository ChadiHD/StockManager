using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Services;
using Xunit;
using Stores = SMPortal.Pages.Admin.Store.Stores;

namespace SMPortal.Tests;

// Creating and opening stores (T9). A store used to be a row inserted by hand, live the moment
// it existed.
public class StoresTests : TestContext
{
    private readonly IAdminDataService _data = Substitute.For<IAdminDataService>();

    public StoresTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_data);
        Services.AddSingleton(Substitute.For<IToastService>());

        _data.ManagesAllStores.Returns(true);
        _data.CurrentSiteKey.Returns("ie-store");
        _data.Sites.Returns(new List<SiteOption>
        {
            new(1, "ie-store", "Irish store", "IE", "EUR"),
            new(2, "uk-store", "UK store", "GB", "GBP", IsActive: false)
        });
        _data.GetStoreSettings().Returns(new StoreSettingsView
        {
            RegistrationFieldSets = ["eu-b2b", "uk-b2b"],
            TaxRuleSets = ["eu-b2b", "uk-b2b"]
        });
    }

    [Fact]
    public void EachStoreSaysWhetherItIsOpenAndOffersTheOtherAction()
    {
        var cut = RenderComponent<Stores>();

        cut.Find(".store-row[data-key=ie-store]").TextContent.Should().Contain("Open").And.Contain("Close");
        cut.FindAll(".store-row[data-key=uk-store] button.open-store").Should().ContainSingle();
    }

    [Fact]
    public void ARefusedOpeningSaysWhatTheStoreStillNeeds()
    {
        _data.SetStoreOpen("uk-store", true).Returns("This store cannot open yet. It still needs its VAT number.");
        var cut = RenderComponent<Stores>();

        cut.Find(".store-row[data-key=uk-store] button.open-store").Click();

        cut.Find(".store-refusal").TextContent.Should().Contain("still needs its VAT number");
    }

    [Fact]
    public void ANewStoreIsCreatedFromWhatTheAdminTyped()
    {
        _data.CreateStore(default!).ReturnsForAnyArgs((new SiteOption(3, "acme-uk", "Acme UK", "GB", "GBP", false), (string?)null));
        var cut = RenderComponent<Stores>();

        cut.Find("button.add-store").Click();
        cut.Find("#new-store-name").Change("Acme UK");
        cut.Find("#new-store-key").Change("acme-uk");
        cut.Find("#new-store-domain").Change("shop.acme.co.uk");
        cut.Find("#new-store-country").Change("GB");
        cut.Find("#new-store-currency").Change("GBP");
        cut.Find("#new-store-locale").Change("en-GB");
        cut.Find("#new-store-tax").Change("uk-b2b");
        cut.Find("button.create-store").Click();

        _data.Received(1).CreateStore(Arg.Is<NewStore>(store =>
            store.SiteKey == "acme-uk" && store.Domain == "shop.acme.co.uk" && store.CurrencyCode == "GBP"
            && store.RegistrationFieldSet == "eu-b2b" && store.TaxRuleSet == "uk-b2b"));
    }

    [Fact]
    public void ClosingAStoreAsksFirst()
    {
        var cut = RenderComponent<Stores>();

        cut.Find(".store-row[data-key=ie-store] button.close-store").Click();
        _data.DidNotReceiveWithAnyArgs().SetStoreOpen(default!, default);

        cut.Find("button.confirm-close").Click();
        _data.Received(1).SetStoreOpen("ie-store", false);
    }

    [Fact]
    public void AnAdminGivenSomeStoresIsOfferedNothing()
    {
        _data.ManagesAllStores.Returns(false);

        var cut = RenderComponent<Stores>();

        cut.Find(".stores-restricted").TextContent.Should().Contain("admin of every store");
        cut.FindAll("button").Should().BeEmpty();
    }
}
