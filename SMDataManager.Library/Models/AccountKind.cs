namespace SMDataManager.Library.Models
{
    /// <summary>
    /// What <c>Account.Kind</c> holds, and <c>CK_Account_Kind</c> allows (T12).
    /// </summary>
    public static class AccountKind
    {
        public const string Business = "Business";

        /// <summary>A person buying for themselves: a guest at checkout, or a b2c registration.</summary>
        public const string Consumer = "Consumer";
    }
}
