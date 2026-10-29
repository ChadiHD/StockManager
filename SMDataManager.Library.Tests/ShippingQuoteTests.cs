using FluentAssertions;
using SMDataManager.Library.Models;
using SMDataManager.Library.Shipping;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>Which delivery rate a checkout charges, and what (T13). Pure: no database.</summary>
public class ShippingQuoteTests
{
    private static readonly ShippingRateModel[] Rates =
    [
        new() { Name = "Brazil", Countries = "BR", Price = 15m, FreeOver = 200m, IsActive = true },
        new() { Name = "World", Countries = null, Price = 40m, IsActive = true },
        new() { Name = "Retired", Countries = "BR", Price = 1m, IsActive = false }
    ];

    [Fact]
    public void TheCheapestActiveRateCoveringTheCountryIsCharged()
    {
        ShippingQuote.Choose(Rates, "br", 50m).Should().Be(new ShippingCharge("Brazil", 15m));
        ShippingQuote.Choose(Rates, "PT", 50m).Should().Be(new ShippingCharge("World", 40m),
            "a rate with no countries covers every one");
    }

    [Fact]
    public void ARateIsWaivedAtItsThresholdOnTheGoods()
    {
        ShippingQuote.Choose(Rates, "BR", 200m)!.Price.Should().Be(0m);
        ShippingQuote.Choose(Rates, "BR", 199.99m)!.Price.Should().Be(15m);
    }

    [Fact]
    public void NoRateCoveringTheCountryIsNoCharge() =>
        ShippingQuote.Choose([Rates[0]], "PT", 50m).Should().BeNull();

    [Theory]
    [InlineData(" br, pt;BR ", "BR,PT")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CountriesAreStoredOnceEachInUpperCase(string? typed, string? stored) =>
        ShippingQuote.Normalise(typed).Should().Be(stored);
}
