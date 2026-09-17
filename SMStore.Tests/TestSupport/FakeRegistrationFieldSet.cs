using SMDataManager.Library.Models;
using SMStore.Registration;

namespace SMStore.Tests.TestSupport;

/// <summary>
/// A field set with nothing behind it but the map it is given. EuB2bRegistrationFieldSet has
/// an entry for every <see cref="RegistrationField"/>, so nothing renders it hides a field —
/// there is no way to see Register.razor's Hidden branch, or two stores disagreeing about what
/// they ask for, without a second field set to compare it against. This is that field set,
/// and it exists only for tests.
/// </summary>
internal sealed class FakeRegistrationFieldSet(
    string key,
    IReadOnlyDictionary<RegistrationField, RegistrationRequirement> requirements,
    IReadOnlyList<RegistrationDocumentRequirement>? documents = null)
    : IRegistrationFieldSet
{
    public string Key { get; } = key;

    public string Summary => "Fake field set for tests.";

    public IReadOnlyList<RegistrationDocumentRequirement> Documents { get; } = documents ?? [];

    public RegistrationRequirement RequirementFor(RegistrationField field) =>
        requirements.TryGetValue(field, out var requirement) ? requirement : RegistrationRequirement.Hidden;

    public string Label(RegistrationField field) => field.ToString();

    // Not exercised: every test using this double drives Register.razor's rendering, not its
    // submission path, and controls the outcome of RegisterAsync directly instead.
    public IReadOnlyList<RegistrationError> Validate(RegistrationSubmission submission, SiteModel site) => [];
}
