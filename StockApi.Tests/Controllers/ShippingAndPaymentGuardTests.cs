using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// What the delivery-rate and payment-key screens may save (T13). Refused here first, because a
/// CHECK violated inside a procedure is a 500, and a publishable key saved as a secret one is a
/// checkout that fails in front of a customer.
/// </summary>
public class ShippingAndPaymentGuardTests
{
    [Theory]
    [InlineData("", 5, null, "a name")]
    [InlineData("Standard", -1, null, "below zero")]
    [InlineData("Standard", 5, "Brazil", "two-letter")]
    public void ARateTheTableWouldRefuseIsRefusedFirst(string name, decimal price, string? countries, string says) =>
        ShippingRateController.WhyRefused(new ShippingRateModel { Name = name, Price = price, Countries = countries })
            .Should().Contain(says);

    [Fact]
    public void ARateIsSavedForTheActingStoreWithItsCountriesTidied()
    {
        var rates = Substitute.For<IShippingRateData>();
        var site = Substitute.For<IAdminSiteContext>();
        site.SiteId.Returns(42);
        rates.Save(Arg.Any<ShippingRateModel>(), 42).Returns(true);

        new ShippingRateController(rates, site).Save(new ShippingRateModel { Name = " Standard ", Price = 5m, Countries = "br, pt" })
            .Should().BeOfType<NoContentResult>();

        rates.Received(1).Save(Arg.Is<ShippingRateModel>(rate => rate.Name == "Standard" && rate.Countries == "BR,PT"), 42);
    }

    [Theory]
    [InlineData("pk_live_123", "whsec_1")]
    [InlineData("sk_live_123", "not-a-secret")]
    public void KeysThatCannotTakePaymentAreRefusedAndNeverStored(string secretKey, string webhookSecret)
    {
        var sites = Substitute.For<ISiteData>();
        var site = Substitute.For<IAdminSiteContext>();
        site.SiteId.Returns(42);

        new SiteController(sites, Substitute.For<IUserData>())
            .SetPayment(new SiteController.PaymentKeysModel(secretKey, webhookSecret), site, new EphemeralDataProtectionProvider())
            .Should().BeOfType<BadRequestObjectResult>();

        sites.DidNotReceiveWithAnyArgs().SetPaymentSecrets(default, default!, default!);
    }

    [Fact]
    public void KeysAreStoredAsCiphertextNeverAsTyped()
    {
        var sites = Substitute.For<ISiteData>();
        sites.SetPaymentSecrets(default, default!, default!).ReturnsForAnyArgs((string?)null);
        var site = Substitute.For<IAdminSiteContext>();
        site.SiteId.Returns(42);

        new SiteController(sites, Substitute.For<IUserData>())
            .SetPayment(new SiteController.PaymentKeysModel("sk_test_abc", "whsec_def"), site, new EphemeralDataProtectionProvider())
            .Should().BeOfType<NoContentResult>();

        sites.Received(1).SetPaymentSecrets(42,
            Arg.Is<string>(stored => stored != "sk_test_abc" && !stored.Contains("sk_test")),
            Arg.Is<string>(stored => stored != "whsec_def"));
    }
}
