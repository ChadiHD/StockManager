using System.Text.RegularExpressions;
using SMDataManager.Library.Models;

namespace SMStore.Registration;

/// <summary>
/// The application a UK business-to-business store asks for (T9): company identity, a billing
/// address with its postcode, and a document proving the business exists.
/// </summary>
/// <remarks>
/// The EU field set does not fit a UK store, and not only on labels. It requires a VAT number,
/// and many small UK businesses trade below the registration threshold without one; and it
/// refuses a GB number outright, since GB left the EU VAT area. Here the VAT number is optional
/// — a UK store charges UK VAT whether or not the customer is registered, so the number decides
/// nothing about the price — and is shape-checked as a UK one when given.
/// </remarks>
public sealed partial class UkB2bRegistrationFieldSet : RegistrationFieldSet
{
    public override string Key => "uk-b2b";

    public override string Summary =>
        "Trade accounts are for businesses. Have your Companies House number to hand if you are a "
        + "limited company, and a document showing the business exists.";

    private static readonly Dictionary<RegistrationField, RegistrationRequirement> Rules = new()
    {
        [RegistrationField.Company] = RegistrationRequirement.Required,
        // Optional: below the threshold a business need not register, and the price a UK store
        // charges does not depend on the number.
        [RegistrationField.VatNumber] = RegistrationRequirement.Optional,
        // Optional: sole traders and most partnerships have no Companies House number.
        [RegistrationField.RegistrationNumber] = RegistrationRequirement.Optional,

        [RegistrationField.FirstName] = RegistrationRequirement.Required,
        [RegistrationField.LastName] = RegistrationRequirement.Required,
        [RegistrationField.Email] = RegistrationRequirement.Required,
        [RegistrationField.Phone] = RegistrationRequirement.Required,

        [RegistrationField.AddressLine1] = RegistrationRequirement.Required,
        [RegistrationField.AddressLine2] = RegistrationRequirement.Optional,
        [RegistrationField.City] = RegistrationRequirement.Required,
        [RegistrationField.Region] = RegistrationRequirement.Optional,
        // Required, unlike the EU set: every UK address has one, and delivery depends on it.
        [RegistrationField.PostCode] = RegistrationRequirement.Required,
        [RegistrationField.Country] = RegistrationRequirement.Required
    };

    protected override IReadOnlyDictionary<RegistrationField, RegistrationRequirement> Requirements => Rules;

    // The document kind is the existing ChamberOfCommerce: CK_AccountDocument_Kind's name for
    // "proof the company exists", whatever the paper is called in a given country. The label is
    // what the applicant reads.
    public override IReadOnlyList<RegistrationDocumentRequirement> Documents { get; } =
    [
        new("ChamberOfCommerce", "Proof the business exists", true,
            "A certificate of incorporation, a Companies House extract, or for a sole trader a recent business bank statement. PDF or an image, up to 10 MB."),
        new("VatCertificate", "VAT registration certificate", false,
            "If you are VAT-registered, this speeds up approval.")
    ];

    public override string Label(RegistrationField field) => field switch
    {
        RegistrationField.RegistrationNumber => "Companies House number",
        RegistrationField.Region => "County",
        RegistrationField.PostCode => "Postcode",
        _ => base.Label(field)
    };

    public override IReadOnlyList<RegistrationError> Validate(
        RegistrationSubmission submission, SiteModel site)
    {
        var errors = base.Validate(submission, site).ToList();

        // Shape only, of what is there: absence is the base class's business. Whether a number
        // exists is checked by a member of staff at approval, which is why approval exists — a
        // form that claimed to have validated it would stop them looking.
        var vat = Compact(submission.VatNumber);

        if (!string.IsNullOrEmpty(vat) && !UkVatNumber().IsMatch(vat))
        {
            errors.Add(new RegistrationError(RegistrationField.VatNumber,
                "A UK VAT number is GB followed by 9 digits, such as GB123456789."));
        }

        var company = Compact(submission.RegistrationNumber);

        if (!string.IsNullOrEmpty(company) && !CompaniesHouseNumber().IsMatch(company))
        {
            errors.Add(new RegistrationError(RegistrationField.RegistrationNumber,
                "A Companies House number is 8 characters: 8 digits, or 2 letters and 6 digits, such as 01234567 or SC123456."));
        }

        return errors;
    }

    private static string? Compact(string? value) =>
        value?.Trim().Replace(" ", string.Empty).ToUpperInvariant();

    // GB + 9 digits, GB + 12 for a branch of a group, or GBGD/GBHA + 3 for government
    // departments and health authorities.
    [GeneratedRegex("^GB([0-9]{9}|[0-9]{12}|GD[0-9]{3}|HA[0-9]{3})$")]
    private static partial Regex UkVatNumber();

    [GeneratedRegex("^([0-9]{8}|[A-Z]{2}[0-9]{6})$")]
    private static partial Regex CompaniesHouseNumber();
}
