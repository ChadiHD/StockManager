using FluentAssertions;
using SMDataManager.Library.Tax;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// The whole reverse-charge decision, as a table.
/// </summary>
/// <remarks>
/// Needs no database, which is the point of a rule set that takes values: the thing most
/// likely to be wrong about tax is the branching, and branching is cheap to drive exhaustively
/// when nothing has to be inserted first.
///
/// What these cannot tell you is whether the rules are *correct* — the design doc says an
/// accountant signs that off before go-live and nobody has. What they hold is that the engine
/// applies the same rules the same way every time, which is what makes a later correction a
/// change in one file rather than an archaeology exercise across every order ever raised.
/// </remarks>
public class TaxRuleSetTests
{
    private readonly EuB2bTaxRuleSet _rules = new();

    private static TaxContext Context(
        string customerCountry, string? vatNumber = null, bool taxable = true,
        string siteCountry = "IE", decimal rate = 23m) =>
        new(siteCountry, rate, customerCountry, vatNumber!, taxable);

    [Fact]
    public void ACustomerInTheStoresOwnCountryPaysTheStandardRate()
    {
        var assessment = _rules.Assess(Context("IE"));

        assessment.Treatment.Should().Be(TaxTreatment.DomesticStandard);
        assessment.RatePct.Should().Be(23m);
        assessment.Legend.Should().Contain("23%");
    }

    [Fact]
    public void ADomesticVatNumberDoesNotBuyTheReverseCharge()
    {
        // The reverse charge is about crossing a border, not about being a business. An Irish
        // company buying from an Irish store pays Irish VAT however many numbers it has.
        _rules.Assess(Context("IE", vatNumber: "IE1234567X")).Treatment
            .Should().Be(TaxTreatment.DomesticStandard);
    }

    [Fact]
    public void AnEuCustomerWithAVatNumberAccountsForTheTaxThemselves()
    {
        var assessment = _rules.Assess(Context("DE", vatNumber: "DE123456789"));

        assessment.Treatment.Should().Be(TaxTreatment.IntraEuReverseCharge);
        assessment.RatePct.Should().Be(0m);

        // The legend is the part that has to appear on the document; an invoice zero-rating a
        // sale without saying why is one the customer's accountant sends back.
        assessment.Legend.Should().Contain("reverse charge");
    }

    [Fact]
    public void AnEuCustomerWithoutAVatNumberPaysTheStandardRate()
    {
        // Not evidenced as a business, so they are treated as a consumer in another member
        // state and pay the seller's rate.
        var assessment = _rules.Assess(Context("DE"));

        assessment.Treatment.Should().Be(TaxTreatment.DomesticStandard);
        assessment.RatePct.Should().Be(23m);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("United States")]
    [InlineData("GB")]
    [InlineData("CH")]
    public void ACustomerOutsideTheUnionIsZeroRatedWhateverTheyProduce(string country)
    {
        // Including with a VAT number: a British company's number is not an EU one, and the
        // sale is an export either way.
        var assessment = _rules.Assess(Context(country, vatNumber: "GB123456789"));

        assessment.Treatment.Should().Be(TaxTreatment.Export);
        assessment.RatePct.Should().Be(0m);
    }

    [Fact]
    public void AProductTheStoreMarkedExemptIsExemptForEverybody()
    {
        // Checked before anything about the customer, and it beats the reverse charge too:
        // Product.IsTaxable is the flag the desktop till already honours, and one product
        // must not be taxable at the counter and exempt on the web.
        var assessment = _rules.Assess(Context("DE", vatNumber: "DE123456789", taxable: false));

        assessment.Treatment.Should().Be(TaxTreatment.NotTaxable);
        assessment.RatePct.Should().Be(0m);
    }

    [Theory]
    [InlineData("Ireland")]
    [InlineData("ireland")]
    [InlineData("  IE  ")]
    public void ACountryNameAndItsCodeAreTheSamePlace(string country)
    {
        // Account.Country is free text collected by a registration field set, so "IE" and
        // "Ireland" both reach here. Treating them as different countries would zero-rate a
        // domestic sale, which is the store's own money.
        _rules.Assess(Context(country)).Treatment.Should().Be(TaxTreatment.DomesticStandard);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnAccountWithNoCountryIsTreatedAsDomestic(string? country)
    {
        // The safe direction. Zero-rating on the strength of a blank field would make an
        // incomplete registration the cheapest way to avoid VAT.
        _rules.Assess(Context(country!)).Treatment.Should().Be(TaxTreatment.DomesticStandard);
    }

    [Theory]
    [InlineData(100, 23, 23.00)]
    [InlineData(92.99, 23, 21.39)]
    // 0.125 at 23% is 0.02875, which rounds away from zero to 0.03 and to even to 0.02.
    [InlineData(0.125, 23, 0.03)]
    public void TaxRoundsTheWayEveryOtherMoneyFigureHereDoes(
        decimal net, decimal rate, decimal expected)
    {
        // MidpointRounding.AwayFromZero, matching PriceResolver and the T-SQL ROUND in
        // fnCatalog_VisibleProducts. A line taxed by one rule and priced by another is two
        // numbers on one document that do not add up.
        new TaxAssessment(TaxTreatment.DomesticStandard, rate, "").On(net)
            .Should().Be(expected);
    }

    [Fact]
    public void TheProviderFindsTheRuleSetASiteNames()
    {
        var provider = new TaxRuleSetProvider(new ITaxRuleSet[] { _rules });

        provider.For(EuB2bTaxRuleSet.RuleSetKey).Should().BeSameAs(_rules);
    }

    [Theory]
    [InlineData("us-sales-tax")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnregisteredRuleSetThrowsRatherThanFallingBack(string? key)
    {
        var provider = new TaxRuleSetProvider(new ITaxRuleSet[] { _rules });

        // Failing loudly beats defaulting, and more so here than for the ordering mode: a
        // site quietly falling back to another store's tax treatment charges the wrong VAT on
        // documents a customer files.
        var resolve = () => provider.For(key!);

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{EuB2bTaxRuleSet.RuleSetKey}*");
    }
}
