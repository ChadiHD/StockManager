using SMDataManager.Library.Models;

namespace SMStore.Registration;

/// <summary>
/// The application an EU business-to-business store asks for: company identity, a VAT number,
/// a billing address, and a document proving the company exists.
/// </summary>
/// <remarks>
/// The VAT number is a required field rather than a nice-to-have because T6's tax engine is
/// built on it. Intra-EU reverse charge applies when a customer in another member state
/// supplies a valid VAT number and not otherwise, so an account without one can only ever be
/// charged domestic rate — which is a commercial problem discovered at invoicing rather than
/// at registration.
/// </remarks>
public sealed class EuB2bRegistrationFieldSet : RegistrationFieldSet
{
    public override string Key => "eu-b2b";

    public override string Summary =>
        "Trade accounts are for registered businesses. You will need your VAT number and a "
        + "recent company registration document.";

    private static readonly Dictionary<RegistrationField, RegistrationRequirement> Rules = new()
    {
        [RegistrationField.Company] = RegistrationRequirement.Required,
        [RegistrationField.VatNumber] = RegistrationRequirement.Required,
        // Optional: the number exists in every member state but under a different name and
        // authority in each, and a customer who cannot find theirs should not be stopped by
        // it when the VAT number already identifies them.
        [RegistrationField.RegistrationNumber] = RegistrationRequirement.Optional,

        [RegistrationField.FirstName] = RegistrationRequirement.Required,
        [RegistrationField.LastName] = RegistrationRequirement.Required,
        [RegistrationField.Email] = RegistrationRequirement.Required,
        [RegistrationField.Phone] = RegistrationRequirement.Required,

        [RegistrationField.AddressLine1] = RegistrationRequirement.Required,
        [RegistrationField.AddressLine2] = RegistrationRequirement.Optional,
        [RegistrationField.City] = RegistrationRequirement.Required,
        [RegistrationField.Region] = RegistrationRequirement.Optional,
        // Optional on purpose. Ireland had no general postcode system until Eircode and
        // adoption is still partial, so a required postcode makes the form unfillable for
        // exactly the customers the first store on this platform is for.
        [RegistrationField.PostCode] = RegistrationRequirement.Optional,
        [RegistrationField.Country] = RegistrationRequirement.Required
    };

    protected override IReadOnlyDictionary<RegistrationField, RegistrationRequirement> Requirements => Rules;

    public override IReadOnlyList<RegistrationDocumentRequirement> Documents { get; } =
    [
        new("ChamberOfCommerce", "Company registration document", true,
            "A certificate of incorporation, a Chamber of Commerce extract, or your CRO printout. PDF or an image, up to 10 MB."),
        new("VatCertificate", "VAT registration certificate", false,
            "Speeds up approval if your VAT number is recent and not yet showing on VIES.")
    ];

    /// <summary>
    /// Member-state VAT prefixes. Greece files as EL rather than GR, and XI is Northern
    /// Ireland, which remains inside the EU VAT area for goods after Brexit — both are the
    /// kind of detail that makes a hand-written list worth having over a country-code lookup.
    /// </summary>
    private static readonly HashSet<string> VatPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "EL", "HU",
        "IE", "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
        "XI"
    };

    public override string Label(RegistrationField field) => field switch
    {
        // What an Irish applicant will be looking for. The generic name sends people hunting
        // for a document that is filed under another title in their own paperwork.
        RegistrationField.RegistrationNumber => "Company registration number (CRO)",
        RegistrationField.Region => "County",
        RegistrationField.PostCode => "Eircode or postcode",
        _ => base.Label(field)
    };

    public override IReadOnlyList<RegistrationError> Validate(
        RegistrationSubmission submission, SiteModel site)
    {
        var errors = base.Validate(submission, site).ToList();

        var vat = submission.VatNumber?.Trim().Replace(" ", string.Empty);

        // Only shape-check what is there. Absence is the base class's business, and
        // complaining twice about one empty box is how a form starts looking broken.
        if (!string.IsNullOrEmpty(vat))
        {
            errors.AddRange(ValidateVatShape(vat, submission.Country));
        }

        return errors;
    }

    /// <summary>
    /// Checks a VAT number looks like one. It does not check that it exists.
    /// </summary>
    /// <remarks>
    /// Real validation is a VIES lookup against the Commission's service, which is a network
    /// call that is frequently down and answers for other member states on a best-effort
    /// basis. That belongs behind an interface with a cached result and a manual override,
    /// and it is not this phase's work — for now a staff member checks it during approval,
    /// which is why approval exists.
    ///
    /// So this catches the transposed prefix and the pasted-in spaces, and nothing more.
    /// Claiming otherwise would be worse than doing nothing: an operator who believes the
    /// form validated a VAT number will not check it themselves.
    /// </remarks>
    private static IEnumerable<RegistrationError> ValidateVatShape(string vat, string? country)
    {
        if (vat.Length < 4 || !char.IsAsciiLetter(vat[0]) || !char.IsAsciiLetter(vat[1]))
        {
            yield return new RegistrationError(RegistrationField.VatNumber,
                "A VAT number starts with a two-letter country code, for example IE1234567X.");
            yield break;
        }

        var prefix = vat[..2];

        if (!VatPrefixes.Contains(prefix))
        {
            yield return new RegistrationError(RegistrationField.VatNumber,
                $"'{prefix}' is not an EU VAT country code. Trade accounts here are for EU-registered businesses.");
            yield break;
        }

        if (vat.Length > 14 || !vat[2..].All(char.IsAsciiLetterOrDigit))
        {
            yield return new RegistrationError(RegistrationField.VatNumber,
                "That VAT number has characters or a length we do not recognise. Check it and try again.");
            yield break;
        }

        // Advisory, not a rejection. A company can be VAT-registered in a member state it
        // does not trade from, so this is usually a typo and occasionally a holding company
        // doing something perfectly legitimate — and telling a real customer they are wrong
        // costs more than letting a staff member notice it at approval.
        if (!string.IsNullOrWhiteSpace(country)
            && !string.Equals(prefix, country.Trim(), StringComparison.OrdinalIgnoreCase)
            // Greece is the one member state whose VAT prefix is not its country code.
            && !(string.Equals(prefix, "EL", StringComparison.OrdinalIgnoreCase)
                 && string.Equals(country.Trim(), "GR", StringComparison.OrdinalIgnoreCase)))
        {
            yield return RegistrationError.Advisory(RegistrationField.VatNumber,
                $"This VAT number is registered in {prefix.ToUpperInvariant()} but the address is in "
                + $"{country.Trim().ToUpperInvariant()}. Check both are right — we can still accept it if they are.");
        }
    }
}
