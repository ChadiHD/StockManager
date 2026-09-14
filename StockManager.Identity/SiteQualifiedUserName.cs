namespace StockManager.Identity
{
    /// <summary>
    /// How a customer login is named, so that one person can hold accounts at two of the
    /// stores run from this platform without those stores learning about each other.
    /// </summary>
    /// <remarks>
    /// ASP.NET Identity has one user table and a unique username. Left at the email address
    /// that makes one person one login globally, which is wrong for a platform hosting
    /// separate businesses: a customer of two stores would share a credential and a password
    /// reset between them, and either store's administrator could infer that the other has
    /// them as a customer. Qualifying the username with the store removes all of that.
    ///
    /// Emails are left non-unique for the same reason, and site membership is still checked
    /// per request through Contact -> Account -> Site. This only stops a store A credential
    /// existing at store B; it is not by itself the authorisation check.
    ///
    /// **Decide this before there are users.** It is a one-line rule today and a migration of
    /// live credentials afterwards.
    /// </remarks>
    public static class SiteQualifiedUserName
    {
        /// <summary>
        /// Separates the store from the email.
        /// </summary>
        /// <remarks>
        /// Every character in Identity's default allow-list that might have served —
        /// <c>- . _ @ +</c> — can legally appear in an email address, and a site key can
        /// contain a hyphen, so no default separator composes injectively: "a-b" + "c@x" and
        /// "a" + "b-c@x" would be one username. A pipe cannot appear in either half.
        /// </remarks>
        public const char Separator = '|';

        /// <summary>
        /// Identity's default allow-list plus <see cref="Separator"/>.
        /// </summary>
        /// <remarks>
        /// Both hosts must set this. They share one user store, and a host whose validator
        /// does not know about the separator would reject a customer username on any update
        /// that revalidates it — a failure that would only appear later, on an unrelated
        /// operation, in the host that never creates these users.
        /// </remarks>
        public const string AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+" + "|";

        /// <summary>The login name for an email address at one store.</summary>
        public static string For(string siteKey, string email) =>
            $"{siteKey}{Separator}{email.Trim()}";
    }
}
