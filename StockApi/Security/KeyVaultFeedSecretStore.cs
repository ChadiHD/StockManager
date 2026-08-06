using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using SMDataManager.Library.Feeds;
using System.Text.RegularExpressions;

namespace StockApi.Security
{
    // Stores the credential in Azure Key Vault and keeps only the secret's NAME in the database.
    // The value never reaches SQL, a database backup, or a log.
    //
    // Uses DefaultAzureCredential, so in Azure Container Apps this runs on the app's managed
    // identity and there is no bootstrap credential to manage at all.
    public class KeyVaultFeedSecretStore : IFeedSecretStore
    {
        public const string Provider = "KeyVault";

        private readonly SecretClient _client;
        private readonly ILogger<KeyVaultFeedSecretStore> _logger;

        public KeyVaultFeedSecretStore(IConfiguration config, ILogger<KeyVaultFeedSecretStore> logger)
        {
            var vaultUri = config["FeedSecrets:KeyVaultUri"]
                ?? throw new InvalidOperationException(
                    "FeedSecrets:KeyVaultUri must be set when the KeyVault secret store is selected.");

            _client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
            _logger = logger;
        }

        public string ProviderName => Provider;

        public async Task<string> ProtectAsync(string feedName, string secret, CancellationToken cancellationToken = default)
        {
            var name = BuildSecretName(feedName);
            await _client.SetSecretAsync(name, secret, cancellationToken);

            // Only the name is returned; the caller persists this, never the value.
            return name;
        }

        public async Task<string> ResolveAsync(string reference, CancellationToken cancellationToken = default)
        {
            var secret = await _client.GetSecretAsync(reference, cancellationToken: cancellationToken);
            return secret.Value.Value;
        }

        public async Task RemoveAsync(string reference, CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.StartDeleteSecretAsync(reference, cancellationToken);
            }
            catch (Exception exception)
            {
                // A feed can still be deleted even if vault cleanup fails; the orphaned secret
                // is harmless and recorded for follow-up.
                _logger.LogWarning(exception, "Could not delete Key Vault secret {Secret}.", reference);
            }
        }

        // Key Vault names allow only alphanumerics and dashes.
        private static string BuildSecretName(string feedName)
        {
            var slug = Regex.Replace(feedName ?? string.Empty, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
            if (slug.Length == 0) slug = "feed";
            if (slug.Length > 90) slug = slug[..90];

            return $"distributor-feed-{slug}";
        }
    }
}
