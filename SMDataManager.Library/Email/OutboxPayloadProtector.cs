using Microsoft.AspNetCore.DataProtection;

namespace SMDataManager.Library.Email
{
    /// <summary>
    /// Encrypts the payloads that carry a credential, and reads them back at dispatch.
    /// </summary>
    /// <remarks>
    /// Data Protection rather than anything in the database, for the reason
    /// <c>DistributorFeed.SecretRef</c> uses it: the key ring lives in <c>ApiAuthDb</c>, so a
    /// backup of <c>SMDatabase</c> restored somewhere else does not carry the means to read
    /// the links in it. The storefront protects and <c>StockApi</c> unprotects, which works
    /// because <c>AddSharedDataProtection</c> gives both hosts one ring and one application
    /// name.
    ///
    /// Moving the ring orphans what was protected against the old one, as it does feed
    /// passwords. Here that costs only the messages still queued: the dispatcher dead-letters
    /// a payload it cannot read rather than retrying it for two hours, and the customer asks
    /// for another link.
    /// </remarks>
    public sealed class OutboxPayloadProtector
    {
        public const string Purpose = "StockManager.EmailOutbox.Payload.v1";

        private readonly IDataProtector _protector;

        public OutboxPayloadProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector(Purpose);
        }

        public string Protect(string payloadJson) => _protector.Protect(payloadJson);

        /// <exception cref="System.Security.Cryptography.CryptographicException">
        /// The key that wrote it is no longer in the ring.
        /// </exception>
        public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
    }
}
