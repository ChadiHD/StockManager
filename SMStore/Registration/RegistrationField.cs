namespace SMStore.Registration;

/// <summary>
/// The fields a customer application can ask for.
/// </summary>
/// <remarks>
/// A closed list rather than an open form builder, because B2B registration is not an
/// arbitrary form: every store collects roughly the same things about a company and the
/// person applying. What varies by jurisdiction is which of them are mandatory, what shape
/// they have to be in, and what paperwork comes with them — so that is what
/// <see cref="IRegistrationFieldSet"/> decides, and this stays fixed.
///
/// Adding a member here means adding it to <see cref="RegistrationSubmission"/>, to the form,
/// and to every field set's requirement map. That friction is deliberate: a field nobody can
/// store is worse than a field nobody asked for.
/// </remarks>
public enum RegistrationField
{
    Company,
    VatNumber,
    RegistrationNumber,

    FirstName,
    LastName,
    Email,
    Phone,

    AddressLine1,
    AddressLine2,
    City,
    Region,
    PostCode,
    Country
}

/// <summary>What a field set demands of one field.</summary>
public enum RegistrationRequirement
{
    /// <summary>Not rendered, not read, not stored. A store that does not ask cannot leak it.</summary>
    Hidden,

    Optional,
    Required
}
