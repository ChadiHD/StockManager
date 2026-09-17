using SMDataManager.Library.Models;

namespace SMStore.Registration;

/// <summary>
/// The checks every field set wants: that required fields were filled in, that what was
/// filled in fits the column it is going into, and that the password was typed twice the
/// same way. Jurisdictional rules go in the derived class.
/// </summary>
public abstract class RegistrationFieldSet : IRegistrationFieldSet
{
    public abstract string Key { get; }
    public abstract string Summary { get; }
    public abstract IReadOnlyList<RegistrationDocumentRequirement> Documents { get; }

    protected abstract IReadOnlyDictionary<RegistrationField, RegistrationRequirement> Requirements { get; }

    public RegistrationRequirement RequirementFor(RegistrationField field) =>
        Requirements.TryGetValue(field, out var requirement)
            ? requirement
            // Absent means unasked. A field set that forgets a field should not accidentally
            // demand it, and should certainly not render an input nothing reads.
            : RegistrationRequirement.Hidden;

    /// <summary>
    /// Longest value each field can be, matching the column it is stored in. Over-length
    /// input is rejected rather than truncated: a VAT number cut to thirty characters is a
    /// wrong VAT number, and silently storing one is worse than refusing it.
    /// </summary>
    private static readonly IReadOnlyDictionary<RegistrationField, int> MaxLengths =
        new Dictionary<RegistrationField, int>
        {
            [RegistrationField.Company] = 200,
            [RegistrationField.VatNumber] = 30,
            [RegistrationField.RegistrationNumber] = 50,
            [RegistrationField.FirstName] = 100,
            [RegistrationField.LastName] = 100,
            [RegistrationField.Email] = 256,
            [RegistrationField.Phone] = 50,
            [RegistrationField.AddressLine1] = 200,
            [RegistrationField.AddressLine2] = 200,
            [RegistrationField.City] = 100,
            [RegistrationField.Region] = 100,
            [RegistrationField.PostCode] = 20,
            [RegistrationField.Country] = 2
        };

    public virtual IReadOnlyList<RegistrationError> Validate(
        RegistrationSubmission submission, SiteModel site)
    {
        var errors = new List<RegistrationError>();

        foreach (var field in Enum.GetValues<RegistrationField>())
        {
            var requirement = RequirementFor(field);

            if (requirement == RegistrationRequirement.Hidden)
            {
                continue;
            }

            var value = submission.Value(field);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (requirement == RegistrationRequirement.Required)
                {
                    errors.Add(new RegistrationError(field, $"{Label(field)} is required."));
                }

                continue;
            }

            if (value.Trim().Length > MaxLengths[field])
            {
                errors.Add(new RegistrationError(field,
                    $"{Label(field)} cannot be longer than {MaxLengths[field]} characters."));
            }
        }

        ValidateEmail(submission, errors);
        ValidateCountry(submission, errors);
        ValidatePassword(submission, errors);

        return errors;
    }

    private void ValidateEmail(RegistrationSubmission submission, List<RegistrationError> errors)
    {
        var email = submission.Email?.Trim();

        if (string.IsNullOrEmpty(email) || RequirementFor(RegistrationField.Email) == RegistrationRequirement.Hidden)
        {
            return;
        }

        // Deliberately shallow. Chasing RFC 5322 with a regular expression is a known way to
        // reject valid addresses and hang on hostile ones, and it would prove nothing anyway:
        // the confirmation email is what establishes that an address exists and belongs to
        // the applicant. This only catches a typo before they wait for mail that cannot
        // arrive.
        var at = email.IndexOf('@');

        bool plausible = at > 0
            && at == email.LastIndexOf('@')
            && at < email.Length - 1
            && email.IndexOf('.', at) > at + 1
            && !email.EndsWith('.')
            && !email.Contains(' ');

        if (!plausible)
        {
            errors.Add(new RegistrationError(RegistrationField.Email,
                "That does not look like an email address."));
        }
    }

    private void ValidateCountry(RegistrationSubmission submission, List<RegistrationError> errors)
    {
        var country = submission.Country?.Trim();

        if (string.IsNullOrEmpty(country) || RequirementFor(RegistrationField.Country) == RegistrationRequirement.Hidden)
        {
            return;
        }

        // Two letters, because the column is nvarchar(2) and because the tax rules in T6
        // compare it against Site.Country. A country stored as "Ireland" cannot be compared
        // with one stored as "IE".
        if (country.Length != 2 || !char.IsAsciiLetter(country[0]) || !char.IsAsciiLetter(country[1]))
        {
            errors.Add(new RegistrationError(RegistrationField.Country,
                "Choose a country from the list."));
        }
    }

    private static void ValidatePassword(RegistrationSubmission submission, List<RegistrationError> errors)
    {
        if (string.IsNullOrEmpty(submission.Password))
        {
            errors.Add(new RegistrationError(null, "Choose a password."));
            return;
        }

        // Strength is ASP.NET Identity's policy and is not repeated here. Two places deciding
        // what a good password is means two places to change and one of them forgotten; if
        // Identity refuses it, registration fails and says so.
        if (submission.Password != submission.ConfirmPassword)
        {
            errors.Add(new RegistrationError(null, "The two passwords do not match."));
        }
    }

    /// <summary>
    /// What to call a field in a message to the applicant. Overridable because a field set
    /// may have a better local name — "Company registration number" is "CRO number" in
    /// Ireland and a customer looking for that phrase will not find it otherwise.
    /// </summary>
    public virtual string Label(RegistrationField field) => field switch
    {
        RegistrationField.Company => "Company name",
        RegistrationField.VatNumber => "VAT number",
        RegistrationField.RegistrationNumber => "Company registration number",
        RegistrationField.FirstName => "First name",
        RegistrationField.LastName => "Last name",
        RegistrationField.Email => "Email address",
        RegistrationField.Phone => "Phone number",
        RegistrationField.AddressLine1 => "Address",
        RegistrationField.AddressLine2 => "Address line 2",
        RegistrationField.City => "Town or city",
        RegistrationField.Region => "County or region",
        RegistrationField.PostCode => "Postcode",
        RegistrationField.Country => "Country",
        _ => field.ToString()
    };
}
