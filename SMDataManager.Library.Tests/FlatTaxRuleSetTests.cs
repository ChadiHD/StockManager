using FluentAssertions;
using SMDataManager.Library.Tax;
using System.Globalization;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// The one-rate rule set (T10). Whether one rate is the right rule for a store is the
/// accountant's sign-off; these hold that it is applied the same way to everybody.
/// </summary>
public class FlatTaxRuleSetTests
{
    private readonly FlatTaxRuleSet _rules = new();

    [Theory]
    [InlineData("BR", null)]
    [InlineData("PT", null)]
    // A VAT number changes nothing: there is no reverse charge to qualify for.
    [InlineData("DE", "DE123456789")]
    [InlineData(null, null)]
    public void EveryBuyerPaysTheStoreRate(string? country, string? vat)
    {
        var assessment = _rules.Assess(new TaxContext("BR", 17.5m, country!, vat!, IsTaxable: true));

        assessment.Treatment.Should().Be(TaxTreatment.DomesticStandard);
        assessment.RatePct.Should().Be(17.5m);
        assessment.Legend.Should().Be("Tax at 17.5%.");
    }

    [Fact]
    public void AProductTheStoreMarkedExemptIsExemptForEverybody()
    {
        var assessment = _rules.Assess(new TaxContext("BR", 17.5m, "BR", null!, IsTaxable: false));

        assessment.Treatment.Should().Be(TaxTreatment.NotTaxable);
        assessment.RatePct.Should().Be(0m);
    }

    [Fact]
    public void TheLegendIsWrittenTheSameWayWhateverTheCultureOfTheHost()
    {
        // Snapshotted onto the order from either host, and the storefront runs in its store's
        // culture since T10. A comma here would make one acceptance store two different legends.
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            _rules.Assess(new TaxContext("BR", 17.5m, "BR", null!, IsTaxable: true))
                .Legend.Should().Be("Tax at 17.5%.");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
