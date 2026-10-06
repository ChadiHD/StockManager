using FluentAssertions;
using SMDataManager.Library.Models;
using SMStore.Registration;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// The uk-b2b field set (T9). The EU one could not serve a UK store: it requires a VAT number,
/// which many small UK businesses do not have, and refuses a GB one, since GB left the EU VAT
/// area — so no British business could have opened an account at all.
/// </summary>
public class UkB2bRegistrationFieldSetTests
{
    private readonly UkB2bRegistrationFieldSet _fieldSet = new();

    private static readonly SiteModel Site = new()
    {
        Id = 1, SiteKey = "uk", Name = "UK store", Country = "GB", CurrencyCode = "GBP",
        Locale = "en-GB", RegistrationFieldSet = "uk-b2b"
    };

    private static RegistrationSubmission Valid() => new()
    {
        Company = "Acme Trading Ltd",
        VatNumber = "GB 123 4567 89",
        RegistrationNumber = "sc123456",
        FirstName = "Ada",
        LastName = "Byron",
        Email = "ada@acme.example",
        Phone = "+44 20 7946 0000",
        AddressLine1 = "1 Market Street",
        City = "Manchester",
        PostCode = "M1 1AA",
        Country = "GB",
        Password = "correct horse battery staple",
        ConfirmPassword = "correct horse battery staple"
    };

    private IReadOnlyList<RegistrationError> Validate(RegistrationSubmission submission) =>
        _fieldSet.Validate(submission, Site);

    private static bool BlocksOn(IReadOnlyList<RegistrationError> errors, RegistrationField field) =>
        errors.Any(error => error.Blocks && error.Field == field);

    [Fact]
    public void AcceptsACompleteApplicationWithAGbVatNumberAndAScottishCompanyNumber()
    {
        var errors = Validate(Valid());

        errors.Where(error => error.Blocks).Should().BeEmpty(
            string.Join("; ", errors.Select(error => $"{error.Field}: {error.Message}")));
    }

    [Fact]
    public void ASoleTraderWithNoVatOrCompanyNumberCanApply()
    {
        var submission = Valid();
        submission.VatNumber = null;
        submission.RegistrationNumber = " ";

        Validate(submission).Where(error => error.Blocks).Should().BeEmpty();
    }

    [Fact]
    public void ThePostcodeIsRequiredBecauseEveryUkAddressHasOne()
    {
        _fieldSet.RequirementFor(RegistrationField.PostCode).Should().Be(RegistrationRequirement.Required);

        var submission = Valid();
        submission.PostCode = null;

        BlocksOn(Validate(submission), RegistrationField.PostCode).Should().BeTrue();
    }

    [Theory]
    [InlineData("IE1234567X")]
    [InlineData("GB12345")]
    [InlineData("123456789")]
    public void AVatNumberThatIsNotAUkOneIsRefused(string vat)
    {
        var submission = Valid();
        submission.VatNumber = vat;

        BlocksOn(Validate(submission), RegistrationField.VatNumber).Should().BeTrue();
    }

    [Theory]
    [InlineData("GB123456789012")]
    [InlineData("GBGD123")]
    [InlineData("gbha 456")]
    public void TheOtherShapesOfUkVatNumberAreAccepted(string vat)
    {
        var submission = Valid();
        submission.VatNumber = vat;

        BlocksOn(Validate(submission), RegistrationField.VatNumber).Should().BeFalse();
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("ABC12345")]
    [InlineData("12-345-678")]
    public void ACompaniesHouseNumberThatIsNotEightCharactersOfTheRightShapeIsRefused(string number)
    {
        var submission = Valid();
        submission.RegistrationNumber = number;

        BlocksOn(Validate(submission), RegistrationField.RegistrationNumber).Should().BeTrue();
    }

    [Fact]
    public void TheFormSpeaksTheApplicantsLanguageAndAsksForProofTheBusinessExists()
    {
        _fieldSet.Label(RegistrationField.RegistrationNumber).Should().Be("Companies House number");
        _fieldSet.Label(RegistrationField.PostCode).Should().Be("Postcode");

        _fieldSet.Documents.Should().ContainSingle(document => document.IsRequired)
            .Which.Kind.Should().Be("ChamberOfCommerce", "it must be a kind CK_AccountDocument_Kind accepts");
    }
}
