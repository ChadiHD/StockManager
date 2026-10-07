using FluentAssertions;
using SMDataManager.Library.Tax;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// The UK rule set's whole decision, as a table (T9). Like <see cref="TaxRuleSetTests"/>, these
/// hold that the rules are applied the same way every time; whether they are the right rules is
/// the accountant's sign-off.
/// </summary>
public class UkTaxRuleSetTests
{
    private readonly UkB2bTaxRuleSet _rules = new();

    private static TaxContext Context(string? customerCountry, string? vatNumber = null, bool taxable = true) =>
        new("GB", 20m, customerCountry!, vatNumber!, taxable);

    [Theory]
    [InlineData("GB")]
    [InlineData("United Kingdom")]
    [InlineData("uk")]
    [InlineData("Scotland")]
    // Northern Ireland is UK VAT territory for a sale from Great Britain.
    [InlineData("Northern Ireland")]
    [InlineData("XI")]
    public void ACustomerInTheUnitedKingdomPaysTheStandardRate(string country)
    {
        var assessment = _rules.Assess(Context(country));

        assessment.Treatment.Should().Be(TaxTreatment.DomesticStandard);
        assessment.RatePct.Should().Be(20m);
        assessment.Legend.Should().Be("VAT at 20%.");
    }

    [Theory]
    [InlineData("IE", "IE1234567T")]
    [InlineData("DE", "DE123456789")]
    [InlineData("US", null)]
    public void GoodsLeavingTheUnitedKingdomAreAZeroRatedExportWhateverTheVatNumber(string country, string? vat)
    {
        // There is no intra-EU reverse charge to qualify for after Brexit: an Irish business
        // buying from a UK store is an export customer, number or not.
        var assessment = _rules.Assess(Context(country, vat));

        assessment.Treatment.Should().Be(TaxTreatment.Export);
        assessment.RatePct.Should().Be(0m);
        assessment.Legend.Should().Contain("Zero-rated export");
    }

    [Fact]
    public void AnAccountWithNoCountryIsDomesticRatherThanZeroRated()
    {
        _rules.Assess(Context(" ")).Treatment.Should().Be(TaxTreatment.DomesticStandard);
        _rules.Assess(Context(null)).Treatment.Should().Be(TaxTreatment.DomesticStandard);
    }

    [Fact]
    public void AProductTheStoreMarkedExemptIsExemptForEverybody()
    {
        _rules.Assess(Context("GB", taxable: false)).Treatment.Should().Be(TaxTreatment.NotTaxable);
        _rules.Assess(Context("IE", taxable: false)).Treatment.Should().Be(TaxTreatment.NotTaxable);
    }

    [Fact]
    public void TheProviderFindsItByTheKeyAStoreNames()
    {
        new TaxRuleSetProvider([new EuB2bTaxRuleSet(), _rules]).For("uk-b2b").Should().BeSameAs(_rules);
    }
}
