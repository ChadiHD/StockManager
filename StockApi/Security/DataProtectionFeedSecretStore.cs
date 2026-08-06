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
            return Task.FromResult(_protector.Unprotect(reference));
        }

        // The ciphertext is the reference, so deleting the feed row removes the secret with it.
        public Task RemoveAsync(string reference, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
