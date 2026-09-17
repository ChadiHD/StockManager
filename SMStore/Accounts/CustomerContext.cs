using SMDataManager.Library.Models;

namespace SMStore.Accounts;

/// <summary>
/// The signed-in customer, if there is one, for the current request.
/// </summary>
/// <remarks>
/// Deliberately a resolved <see cref="ContactModel"/> rather than a <c>ClaimsPrincipal</c>.
/// A principal says who authenticated; this says which account of which store they belong to,
/// which is the question everything downstream actually asks. Claims cannot answer it on
/// their own — see <see cref="CustomerSessionValidator"/> for why they are not trusted to.
/// </remarks>
public interface ICustomerContext
{
    bool IsSignedIn { get; }

    /// <summary>The contact, or null for an anonymous visitor.</summary>
    ContactModel? Contact { get; }

    int? AccountId { get; }

    /// <summary>
    /// The account's pricing group, or null. Drives both the discount and the catalog
    /// visibility rules, so it must come from here and never from anything a client sends.
    /// </summary>
    int? CustomerGroupId { get; }

    /// <summary>
    /// The group's discount percentage, or zero when anonymous or ungrouped.
    /// </summary>
    /// <remarks>
    /// This and <see cref="CustomerGroupId"/> are read together by
    /// <c>CatalogPresenter.Resolve</c>, and they have to agree with what
    /// <c>dbo.fnCatalog_VisibleProducts</c> resolved for the same group when it sorted the
    /// page. Both come from the same row, so they cannot disagree — but a future caller
    /// tempted to take the rate from somewhere cheaper should know that is the constraint.
    /// </remarks>
    decimal GroupDiscountPct { get; }
}

public sealed class CustomerContext : ICustomerContext
{
    private ContactModel? _contact;

    public bool IsSignedIn => _contact is not null;

    public ContactModel? Contact => _contact;

    public int? AccountId => _contact?.AccountId;

    public int? CustomerGroupId => _contact?.CustomerGroupId;

    public decimal GroupDiscountPct => _contact?.CustomerGroupDiscount ?? 0m;

    internal void SignedInAs(ContactModel contact) => _contact = contact;
}
