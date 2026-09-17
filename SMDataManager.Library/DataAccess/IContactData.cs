using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// People who sign in on behalf of a trading account. See <see cref="IAccountData"/> for
    /// why siteId is mandatory and last on every method.
    /// </summary>
    /// <remarks>
    /// Contact carries no site of its own — it inherits its account's — so every procedure
    /// here joins Account for the predicate rather than trusting the caller to have looked
    /// the account up first. A guessed id returns nothing instead of another store's
    /// customer names and email addresses.
    /// </remarks>
    public interface IContactData
    {
        List<ContactModel> GetByAccount(int accountId, int siteId);

        /// <summary>
        /// The contact behind an authenticated user, or null if that user belongs to no
        /// account at this store.
        /// </summary>
        /// <remarks>
        /// This is the check that makes a storefront session safe. Identity has no concept of
        /// a site and the Data Protection ring is shared between hosts, so a valid cookie
        /// proves identity and says nothing about which store it is good for. Returning null
        /// here is what turns a cross-store cookie into no session at all.
        ///
        /// The result carries the account's status rather than filtering on it, so a caller
        /// can distinguish "not a customer here" from "not approved yet". It must still
        /// refuse anything but Approved.
        /// </remarks>
        ContactModel GetByIdentityUser(string identityUserId, int siteId);

        ContactModel Insert(ContactModel contact, int siteId);
        void Update(ContactModel contact, int siteId);
    }
}
