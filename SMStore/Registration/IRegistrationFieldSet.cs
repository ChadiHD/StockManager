using SMDataManager.Library.Models;

namespace SMStore.Registration;

/// <summary>
/// What a store demands of a customer applying for a trading account, and whether a given
/// application satisfies it.
/// </summary>
/// <remarks>
/// Named by <c>dbo.Site.RegistrationFieldSet</c> and picked by
/// <see cref="RegistrationFieldSetProvider"/>, exactly as <c>IOrderingMode</c> is picked from
/// <c>Site.OrderMode</c>.
///
/// Code rather than configuration rows, and that is the substantive choice here. A field set
/// carries validation, not just layout: a VAT number has a per-country shape, an EU
/// application demands a Chamber of Commerce extract and an export one does not. Expressing
/// that as data means either a rules engine nobody asked for or a table of regexes with no
/// tests, and this codebase already has the pattern for per-site behaviour.
///
/// Nothing outside this folder should branch on <c>Site.RegistrationFieldSet</c>.
/// </remarks>
public interface IRegistrationFieldSet
{
    /// <summary>Matches the value stored in <c>dbo.Site.RegistrationFieldSet</c>.</summary>
    string Key { get; }

    /// <summary>
    /// One sentence for the top of the form, telling an applicant what they will need before
    /// they start rather than discovering it at the upload step.
    /// </summary>
    string Summary { get; }

    /// <summary>
    /// Whether a field is hidden, optional or required. A hidden field is not rendered and
    /// not stored — a store that does not ask cannot leak it.
    /// </summary>
    RegistrationRequirement RequirementFor(RegistrationField field);

    /// <summary>
    /// What to call a field, both on the form and in a message about it.
    /// </summary>
    /// <remarks>
    /// On the interface rather than only on the base class because the form renders from
    /// this, and a label that differed between the input and the error under it would be
    /// two names for one box.
    /// </remarks>
    string Label(RegistrationField field);

    /// <summary>The paperwork this store wants alongside the form.</summary>
    IReadOnlyList<RegistrationDocumentRequirement> Documents { get; }

    /// <summary>
    /// Everything wrong with an application, or an empty list.
    /// </summary>
    /// <remarks>
    /// Returns all the problems rather than the first, because a form that reveals one error
    /// per submission is a form people abandon. The site is passed because some rules are
    /// relative to the store: whether an applicant is domestic or cross-border decides how
    /// their VAT number is read.
    ///
    /// This is the server-side check and the only one that counts. Whatever the browser does
    /// is a convenience.
    /// </remarks>
    IReadOnlyList<RegistrationError> Validate(RegistrationSubmission submission, SiteModel site);
}

/// <summary>
/// A document a store wants with an application.
/// </summary>
/// <param name="Kind">
/// Must be one of the values <c>CK_AccountDocument_Kind</c> allows, since it is stored as
/// <c>dbo.AccountDocument.Kind</c> verbatim.
/// </param>
/// <param name="Label">What to call it on the form.</param>
/// <param name="IsRequired">Whether an application without it can be submitted at all.</param>
/// <param name="Help">One line on what would satisfy this, shown under the control.</param>
public sealed record RegistrationDocumentRequirement(
    string Kind,
    string Label,
    bool IsRequired,
    string Help);

/// <summary>
/// Something to tell the applicant about their application.
/// </summary>
/// <param name="Field">
/// The field to show it against, or null for something wrong with the application as a whole
/// — a mismatched password confirmation belongs to neither box on its own.
/// </param>
/// <param name="Message">
/// Shown to the applicant, so it says what to do rather than what failed. It is also the only
/// feedback they get, since nothing about a rejected form is emailed.
/// </param>
/// <param name="Blocks">
/// Whether this stops the application being submitted.
/// </param>
/// <remarks>
/// Non-blocking notices exist because some checks are worth surfacing and not worth refusing
/// over. A VAT number registered in a different member state from the billing address is
/// usually a mistake and occasionally a holding company doing something entirely legitimate;
/// telling a real customer they are wrong costs more than letting a staff member notice it at
/// approval. A form that can only reject has to choose between blocking those customers and
/// saying nothing.
///
/// Callers submit when nothing <see cref="Blocks"/>, and render everything.
/// </remarks>
public sealed record RegistrationError(RegistrationField? Field, string Message, bool Blocks = true)
{
    /// <summary>A notice worth showing that does not stop submission.</summary>
    public static RegistrationError Advisory(RegistrationField? field, string message) =>
        new(field, message, Blocks: false);
}
