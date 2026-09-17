namespace SMDataManager.Library.Models
{
    /// <summary>
    /// A trading application as it is written: the company, the person applying, and the
    /// address they applied from.
    /// </summary>
    /// <remarks>
    /// Flat rather than three nested models because it is one transaction. Splitting it into
    /// Account, Contact and Address would suggest they can be written separately, and the
    /// whole point of spAccount_Register is that they cannot.
    /// </remarks>
    public class AccountRegistration
    {
        public int SiteId { get; set; }

        /// <summary>
        /// The AspNetUsers.Id the caller has already created. Registration is not atomic
        /// across the two databases, so the caller owns deleting this again if the write
        /// below fails.
        /// </summary>
        public string IdentityUserId { get; set; }

        public string Company { get; set; }
        public string VatNumber { get; set; }
        public string RegistrationNumber { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }

        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string City { get; set; }
        public string Region { get; set; }
        public string PostCode { get; set; }
        public string Country { get; set; }
    }

    /// <summary>What a successful registration produced.</summary>
    public class AccountRegistrationResult
    {
        public int AccountId { get; set; }
        public int ContactId { get; set; }

        /// <summary>The generated AC-nnnn reference, for the acknowledgement email.</summary>
        public string Reference { get; set; }
    }
}
