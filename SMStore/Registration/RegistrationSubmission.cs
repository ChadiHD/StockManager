namespace SMStore.Registration;

/// <summary>
/// What a customer typed into the registration form, before anything has been validated or
/// written.
/// </summary>
/// <remarks>
/// Every property is nullable and none is trusted. The field set decides which have to be
/// present and what shape they take; this type only carries them.
/// </remarks>
public sealed class RegistrationSubmission
{
    public string? Company { get; set; }
    public string? VatNumber { get; set; }
    public string? RegistrationNumber { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostCode { get; set; }

    /// <summary>ISO 3166-1 alpha-2, matching dbo.Address.Country and dbo.Site.Country.</summary>
    public string? Country { get; set; }

    /// <summary>
    /// The credential for the Identity user that registration creates.
    /// </summary>
    /// <remarks>
    /// Not governed by the field set and deliberately absent from
    /// <see cref="RegistrationField"/>. Password policy belongs to ASP.NET Identity and is the
    /// same everywhere; nothing about it is jurisdictional, and a field set that could make a
    /// password optional would be a field set that could disable authentication.
    /// </remarks>
    public string? Password { get; set; }

    public string? ConfirmPassword { get; set; }

    /// <summary>
    /// Reads the value of one field, so validation and the form can address them uniformly
    /// rather than each carrying a switch over every member.
    /// </summary>
    public string? Value(RegistrationField field) => field switch
    {
        RegistrationField.Company => Company,
        RegistrationField.VatNumber => VatNumber,
        RegistrationField.RegistrationNumber => RegistrationNumber,
        RegistrationField.FirstName => FirstName,
        RegistrationField.LastName => LastName,
        RegistrationField.Email => Email,
        RegistrationField.Phone => Phone,
        RegistrationField.AddressLine1 => AddressLine1,
        RegistrationField.AddressLine2 => AddressLine2,
        RegistrationField.City => City,
        RegistrationField.Region => Region,
        RegistrationField.PostCode => PostCode,
        RegistrationField.Country => Country,

        // Unreachable while the switch covers the enum, and a compile-time list is not
        // available here — so it throws rather than returning null, which would read as
        // "the customer left it blank" and silently pass a required-field check.
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unmapped registration field.")
    };
}
