using FluentAssertions;
using SMDataManager.Library.Models;
using SMStore.Registration;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// The eu-b2b field set decides what a customer application must contain before it can be
/// submitted. Untested, a required field quietly stops being required and nobody notices
/// until an account reaches approval with no evidence behind it.
/// </summary>
public class EuB2bRegistrationFieldSetTests
{
    private readonly EuB2bRegistrationFieldSet _fieldSet = new();

    private static readonly SiteModel Site = new()
    {
        Id = 1,
        SiteKey = "test",
        Name = "Test store",
        Country = "IE",
        CurrencyCode = "EUR",
        Locale = "en-IE",
        RegistrationFieldSet = "eu-b2b"
    };

    /// <summary>An application with nothing wrong with it, for tests to spoil one field of.</summary>
    private static RegistrationSubmission Valid() => new()
    {
        Company = "Acme Trading Limited",
        VatNumber = "IE1234567X",
        RegistrationNumber = "123456",
        FirstName = "Ada",
        LastName = "Byron",
        Email = "ada@acme.example",
        Phone = "+353 1 555 0100",
        AddressLine1 = "1 Merrion Square",
        City = "Dublin",
        Country = "IE",
        Password = "correct horse battery staple",
        ConfirmPassword = "correct horse battery staple"
    };

    private IReadOnlyList<RegistrationError> Validate(RegistrationSubmission submission) =>
        _fieldSet.Validate(submission, Site);

    private static bool Blocks(IReadOnlyList<RegistrationError> errors) =>
        errors.Any(error => error.Blocks);

    private static bool BlocksOn(IReadOnlyList<RegistrationError> errors, RegistrationField field) =>
        errors.Any(error => error.Blocks && error.Field == field);

    [Fact]
    public void AcceptsACompleteApplication()
    {
        var errors = Validate(Valid());

        Blocks(errors).Should().BeFalse(
            "complete application was rejected: "
            + string.Join("; ", errors.Select(error => $"{error.Field}: {error.Message}")));
    }

    [Theory]
    [InlineData(RegistrationField.Company)]
    [InlineData(RegistrationField.VatNumber)]
    [InlineData(RegistrationField.FirstName)]
    [InlineData(RegistrationField.LastName)]
    [InlineData(RegistrationField.Email)]
    [InlineData(RegistrationField.Phone)]
    [InlineData(RegistrationField.AddressLine1)]
    [InlineData(RegistrationField.City)]
    [InlineData(RegistrationField.Country)]
    public void DemandsTheFieldsItSaysAreRequired(RegistrationField field)
    {
        _fieldSet.RequirementFor(field).Should().Be(RegistrationRequirement.Required);

        var submission = Valid();
        Clear(submission, field);

        BlocksOn(Validate(submission), field).Should().BeTrue(
            $"{field} is declared Required but a blank one was accepted.");
    }

    [Theory]
    [InlineData(RegistrationField.RegistrationNumber)]
    [InlineData(RegistrationField.AddressLine2)]
    [InlineData(RegistrationField.Region)]
    // Ireland had no general postcode system until Eircode and adoption is still partial, so
    // a required postcode would make the form unfillable for the first store on this platform.
    [InlineData(RegistrationField.PostCode)]
    public void LetsOptionalFieldsBeBlank(RegistrationField field)
    {
        _fieldSet.RequirementFor(field).Should().Be(RegistrationRequirement.Optional);

        var submission = Valid();
        Clear(submission, field);

        Blocks(Validate(submission)).Should().BeFalse();
    }

    [Fact]
    public void RejectsAValueTooLongForItsColumn()
    {
        var submission = Valid();
        submission.Company = new string('x', 201);

        BlocksOn(Validate(submission), RegistrationField.Company).Should().BeTrue();
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("two@at@signs.example")]
    [InlineData("trailing@dot.")]
    [InlineData("has space@example.com")]
    [InlineData("@example.com")]
    public void RejectsAnImplausibleEmail(string email)
    {
        var submission = Valid();
        submission.Email = email;

        BlocksOn(Validate(submission), RegistrationField.Email).Should().BeTrue();
    }

    [Fact]
    public void RejectsMismatchedPasswords()
    {
        var submission = Valid();
        submission.ConfirmPassword = "something else";

        var errors = Validate(submission);

        // Belongs to neither box, so it is reported against the form rather than a field.
        errors.Should().Contain(error => error.Blocks && error.Field == null);
    }

    [Theory]
    [InlineData("IE1234567X")]
    [InlineData("ie1234567x")]
    [InlineData("IE 1234 567X")]
    [InlineData("DE123456789")]
    // Greece files as EL, not GR — the detail a country-code lookup would get wrong.
    [InlineData("EL123456789")]
    // Northern Ireland stays inside the EU VAT area for goods after Brexit.
    [InlineData("XI123456789")]
    public void AcceptsAWellFormedVatNumber(string vat)
    {
        var submission = Valid();
        submission.VatNumber = vat;
        submission.Country = vat.Trim()[..2].ToUpperInvariant() switch
        {
            "EL" => "GR",
            "XI" => "GB",
            var prefix => prefix
        };

        BlocksOn(Validate(submission), RegistrationField.VatNumber).Should().BeFalse();
    }

    [Theory]
    [InlineData("1234567X")]        // no country prefix
    [InlineData("US1234567")]       // not an EU VAT prefix
    [InlineData("ZZ1234567")]       // not a country at all
    [InlineData("IE")]              // prefix only
    [InlineData("IE1234567890123")] // longer than any member state issues
    [InlineData("IE12345_67")]      // punctuation
    public void RejectsAMalformedVatNumber(string vat)
    {
        var submission = Valid();
        submission.VatNumber = vat;

        BlocksOn(Validate(submission), RegistrationField.VatNumber).Should().BeTrue(
            $"'{vat}' was accepted as a VAT number.");
    }

    [Fact]
    public void WarnsWithoutBlockingWhenTheVatCountryDiffersFromTheAddress()
    {
        var submission = Valid();
        submission.VatNumber = "DE123456789";
        submission.Country = "IE";

        var errors = Validate(submission);

        // A company can be VAT-registered in a member state it does not trade from. Usually a
        // typo, occasionally legitimate — so it is surfaced and approval decides.
        errors.Should().Contain(error => error.Field == RegistrationField.VatNumber && !error.Blocks);
        Blocks(errors).Should().BeFalse();
    }

    [Fact]
    public void DemandsProofTheCompanyExists()
    {
        var required = _fieldSet.Documents.Where(document => document.IsRequired).ToList();

        required.Should().NotBeEmpty();

        // Stored verbatim as dbo.AccountDocument.Kind, so a value CK_AccountDocument_Kind
        // does not allow would fail at the insert rather than here.
        string[] allowed = ["VatCertificate", "ChamberOfCommerce", "Other"];
        _fieldSet.Documents.Should().OnlyContain(document => allowed.Contains(document.Kind));
    }

    private static void Clear(RegistrationSubmission submission, RegistrationField field)
    {
        switch (field)
        {
            case RegistrationField.Company: submission.Company = null; break;
            case RegistrationField.VatNumber: submission.VatNumber = null; break;
            case RegistrationField.RegistrationNumber: submission.RegistrationNumber = null; break;
            case RegistrationField.FirstName: submission.FirstName = null; break;
            case RegistrationField.LastName: submission.LastName = null; break;
            case RegistrationField.Email: submission.Email = null; break;
            case RegistrationField.Phone: submission.Phone = null; break;
            case RegistrationField.AddressLine1: submission.AddressLine1 = null; break;
            case RegistrationField.AddressLine2: submission.AddressLine2 = null; break;
            case RegistrationField.City: submission.City = null; break;
            case RegistrationField.Region: submission.Region = null; break;
            case RegistrationField.PostCode: submission.PostCode = null; break;
            case RegistrationField.Country: submission.Country = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }
}
