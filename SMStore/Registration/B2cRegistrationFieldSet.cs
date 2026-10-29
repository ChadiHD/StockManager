using static SMStore.Localization.StoreText;

namespace SMStore.Registration;

/// <summary>
/// A consumer store's account (T12): a name, an email address and a password, and nothing else.
/// </summary>
/// <remarks>
/// No company, no tax number and no paperwork, because a person buying for themselves has none
/// to give and the store has nothing to review. The address is asked for at checkout, where it
/// is needed, rather than here, where it would be a second copy to keep in step. The account is
/// approved at once (<see cref="IsConsumer"/>); email confirmation is still required.
/// </remarks>
public sealed class B2cRegistrationFieldSet : RegistrationFieldSet
{
    public override string Key => "b2c";

    public override string Summary =>
        L["An account keeps your orders in one place. You can also check out without one."];

    public override bool IsConsumer => true;

    private static readonly Dictionary<RegistrationField, RegistrationRequirement> Rules = new()
    {
        [RegistrationField.Company] = RegistrationRequirement.Hidden,
        [RegistrationField.VatNumber] = RegistrationRequirement.Hidden,
        [RegistrationField.RegistrationNumber] = RegistrationRequirement.Hidden,

        [RegistrationField.FirstName] = RegistrationRequirement.Required,
        [RegistrationField.LastName] = RegistrationRequirement.Required,
        [RegistrationField.Email] = RegistrationRequirement.Required,
        [RegistrationField.Phone] = RegistrationRequirement.Optional,

        [RegistrationField.AddressLine1] = RegistrationRequirement.Hidden,
        [RegistrationField.AddressLine2] = RegistrationRequirement.Hidden,
        [RegistrationField.City] = RegistrationRequirement.Hidden,
        [RegistrationField.Region] = RegistrationRequirement.Hidden,
        [RegistrationField.PostCode] = RegistrationRequirement.Hidden,
        [RegistrationField.Country] = RegistrationRequirement.Hidden
    };

    protected override IReadOnlyDictionary<RegistrationField, RegistrationRequirement> Requirements => Rules;

    public override IReadOnlyList<RegistrationDocumentRequirement> Documents => [];
}
