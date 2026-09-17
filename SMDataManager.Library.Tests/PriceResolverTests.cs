using FluentAssertions;
using SMDataManager.Library.Pricing;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// PriceResolver turns a list price into what one account pays, shared by the storefront and
/// admin quoting — see the remarks on IPriceResolver. A quiet drift here is a customer quoted
/// one price on the catalog and another on their quote, so every branch gets its own case
/// rather than relying on the SQL parity test alone to notice.
/// </summary>
public class PriceResolverTests
{
    private readonly IPriceResolver _resolver = new PriceResolver();

    [Fact]
    public void AppliesTheGroupDiscountToTheListPrice()
    {
        var result = _resolver.Resolve(100.00m, cost: null, groupDiscountPct: 20m, minMarginPct: 0m);

        result.NetPrice.Should().Be(80.00m);
        result.EffectiveDiscountPct.Should().Be(20.00m);
        result.FloorApplied.Should().BeFalse();
    }

    [Fact]
    public void ClampsADiscountAbove100RatherThanSellingAtANegativePrice()
    {
        var result = _resolver.Resolve(100.00m, cost: null, groupDiscountPct: 150m, minMarginPct: 0m);

        result.NetPrice.Should().Be(0.00m);
        result.EffectiveDiscountPct.Should().Be(100.00m);
    }

    [Fact]
    public void ClampsANegativeDiscountToZeroRatherThanMarkingThePriceUp()
    {
        var result = _resolver.Resolve(100.00m, cost: null, groupDiscountPct: -10m, minMarginPct: 0m);

        result.NetPrice.Should().Be(100.00m);
        result.EffectiveDiscountPct.Should().Be(0.00m);
    }

    [Fact]
    public void AppliesTheMarginFloorWhenTheDiscountedPriceWouldSellBelowCost()
    {
        // 50% off a 100 list undercuts an 80 cost with a 20% margin requirement (floor 96).
        var result = _resolver.Resolve(100.00m, cost: 80.00m, groupDiscountPct: 50m, minMarginPct: 20m);

        result.NetPrice.Should().Be(96.00m);
        result.EffectiveDiscountPct.Should().Be(4.00m);
        result.FloorApplied.Should().BeTrue();
    }

    [Fact]
    public void LeavesTheGroupPriceAloneWhenItAlreadyClearsTheFloor()
    {
        // 10% off a 100 list (net 90) already clears an 80 cost with a 5% margin (floor 84).
        var result = _resolver.Resolve(100.00m, cost: 80.00m, groupDiscountPct: 10m, minMarginPct: 5m);

        result.NetPrice.Should().Be(90.00m);
        result.FloorApplied.Should().BeFalse();
    }

    [Fact]
    public void CapsTheFloorAtListPriceRatherThanRaisingThePriceAboveIt()
    {
        // Cost 140 plus a 20% margin (168) exceeds the 100 list. The mispriced product is
        // capped at list rather than pushed above it; the operator finds it on the margin
        // report rather than through a customer complaint.
        var result = _resolver.Resolve(100.00m, cost: 140.00m, groupDiscountPct: 10m, minMarginPct: 20m);

        result.NetPrice.Should().Be(100.00m);
        result.EffectiveDiscountPct.Should().Be(0.00m);
        result.FloorApplied.Should().BeTrue();
    }

    [Fact]
    public void DoesNotFlagTheFloorAsAppliedWhenTheCappedFloorMerelyMatchesAnUndiscountedList()
    {
        // Same mispriced product with no group discount: net is already list, so the capped
        // floor (also list) changes nothing, and FloorApplied stays false.
        var result = _resolver.Resolve(100.00m, cost: 140.00m, groupDiscountPct: 0m, minMarginPct: 20m);

        result.NetPrice.Should().Be(100.00m);
        result.FloorApplied.Should().BeFalse();
    }

    [Fact]
    public void SkipsTheFloorWhenCostIsUnknown()
    {
        // A feed row with no cost gets the group's rate as configured, not a floor invented
        // from nothing — see the remarks on Resolve.
        var result = _resolver.Resolve(100.00m, cost: null, groupDiscountPct: 50m, minMarginPct: 50m);

        result.NetPrice.Should().Be(50.00m);
        result.FloorApplied.Should().BeFalse();
    }

    [Fact]
    public void SkipsTheFloorWhenCostIsZero()
    {
        var result = _resolver.Resolve(100.00m, cost: 0.00m, groupDiscountPct: 50m, minMarginPct: 50m);

        result.NetPrice.Should().Be(50.00m);
        result.FloorApplied.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void SkipsTheFloorWhenTheSiteHasNoMinimumMargin(decimal minMarginPct)
    {
        var result = _resolver.Resolve(100.00m, cost: 80.00m, groupDiscountPct: 90m, minMarginPct: minMarginPct);

        result.NetPrice.Should().Be(10.00m);
        result.FloorApplied.Should().BeFalse();
    }

    [Fact]
    public void ReturnsAZeroPriceForAZeroListPriceRatherThanDividingByZero()
    {
        var result = _resolver.Resolve(0.00m, cost: 10.00m, groupDiscountPct: 0m, minMarginPct: 50m);

        result.NetPrice.Should().Be(0.00m);
        result.EffectiveDiscountPct.Should().Be(0.00m);
        result.FloorApplied.Should().BeFalse();
    }

    [Fact]
    public void ThrowsForANegativeListPriceRatherThanReturningANegativePrice()
    {
        var act = () => _resolver.Resolve(-1.00m, cost: null, groupDiscountPct: 0m, minMarginPct: 0m);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("listPrice");
    }

    [Fact]
    public void RoundsAwayFromZeroRatherThanToEven()
    {
        // 12.345 sits on a rounding midpoint where the two .NET modes disagree: to-even keeps
        // the 4 (12.34), away-from-zero — what a price list uses on paper — takes it up (12.35).
        var result = _resolver.Resolve(12.345m, cost: null, groupDiscountPct: 0m, minMarginPct: 0m);

        result.NetPrice.Should().Be(12.35m);
    }

    [Fact]
    public void CanReportANegativeEffectiveDiscountWhenRoundingLiftsTheNetPriceAboveTheUnroundedList()
    {
        // Money carries more precision than a displayed price. EffectiveDiscountPct is
        // recomputed from the rounded net price against the raw (unrounded) list, so a list
        // price with a half-cent can round up past itself and show as a small negative
        // discount even though no discount was requested. See the parity test's own remarks on
        // this exact fixture value for why it is capped against the unrounded list elsewhere.
        var result = _resolver.Resolve(10.005m, cost: null, groupDiscountPct: 0m, minMarginPct: 0m);

        result.NetPrice.Should().Be(10.01m);
        result.EffectiveDiscountPct.Should().Be(-0.05m);
    }

    [Fact]
    public void AFullDiscountSellsAtZero()
    {
        var result = _resolver.Resolve(100.00m, cost: null, groupDiscountPct: 100m, minMarginPct: 0m);

        result.NetPrice.Should().Be(0.00m);
        result.EffectiveDiscountPct.Should().Be(100.00m);
    }
}
