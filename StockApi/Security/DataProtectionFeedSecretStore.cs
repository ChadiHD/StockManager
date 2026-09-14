using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SMDataManager.Library.Feeds;

namespace StockApi.Security
{
    // Encrypts the credential with ASP.NET Core Data Protection and stores the ciphertext as
    // the feed's SecretRef. Suitable for development and single-tenant deployments.
    //
    // IMPORTANT: the protection keyring must be persisted somewhere durable. With the default
    // provider the keys live on the container filesystem, so a rebuilt container can no longer
    // decrypt anything written before it. Program.cs configures persistence; see the comment
    // there before deploying this store to production.
    public class DataProtectionFeedSecretStore : IFeedSecretStore
    {
        public const string Provider = "DataProtection";

        private readonly IDataProtector _protector;

        public DataProtectionFeedSecretStore(IDataProtectionProvider provider)
        {
            // The purpose string scopes the key material to this use only.
            _protector = provider.CreateProtector("StockManager.DistributorFeed.v1");
        }

        public string ProviderName => Provider;

        public Task<string> ProtectAsync(string feedName, string secret, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_protector.Protect(secret));
        }

        public Task<string> ResolveAsync(string reference, CancellationToken cancellationToken = default)
        {
            try
            {
                return Task.FromResult(_protector.Unprotect(reference));
            }
            catch (CryptographicException exception)
            {
                // The ciphertext names a key the current ring does not hold. This is what
                // moving the key ring looks like from here — credentials written against the
                // old ring are simply unreadable, and the raw message ("The key {guid} was not
                // found in the key ring") gives an operator nothing to act on.
                //
                // There is no recovery short of re-entering the password, which re-encrypts
                // against the current ring, so say that.
                throw new InvalidOperationException(
                    "This feed's stored password was encrypted with a Data Protection key that is " +
                    "no longer in the key ring, so it cannot be decrypted. Open the feed in the " +
                    "admin portal, re-enter its password and save — that stores it against the " +
                    "current key ring and the problem does not recur.",
                    exception);
            }
        }

        // The ciphertext is the reference, so deleting the feed row removes the secret with it.
        public Task RemoveAsync(string reference, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
