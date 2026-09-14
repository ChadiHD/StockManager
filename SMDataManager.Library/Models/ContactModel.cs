using System;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// A person who signs in on behalf of a trading account. Distinct from
    /// <see cref="UserModel"/>, which is staff.
    /// </summary>
    public class ContactModel
    {
        public int Id { get; set; }
        public int AccountId { get; set; }

        /// <summary>
        /// The AspNetUsers.Id this contact authenticates as, or null for a contact on file
        /// who has no login. Identity lives in a different database, so nothing enforces that
        /// this still resolves.
        /// </summary>
        public string IdentityUserId { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }

        /// <summary>"Admin" manages the account's contacts and addresses; "Buyer" does not.</summary>
        public string RoleInAccount { get; set; }

        public bool IsPrimary { get; set; }

        /// <summary>"Active" | "Invited" | "Disabled", independent of the account's own status.</summary>
        public string Status { get; set; }

        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Populated only by spContact_GetByIdentityUser, which resolves a signed-in user all
        /// the way to the account behind them.
        /// </summary>
        /// <remarks>
        /// AccountStatus is returned rather than filtered on so a caller can tell "no such
        /// user at this store" from "your application has not been approved yet" and say
        /// something useful. It must still refuse anything but Approved.
        /// </remarks>
        public int? CustomerGroupId { get; set; }

        /// <summary>
        /// The group's discount percentage, or zero for an account with no group. Carried
        /// alongside the id because the storefront resolves the displayed price in C# and
        /// cannot do that from an id alone.
        /// </summary>
        public decimal CustomerGroupDiscount { get; set; }

        public string AccountStatus { get; set; }
        public string AccountCompany { get; set; }

        public string FullName => $"{FirstName} {LastName}".Trim();
    }
}
