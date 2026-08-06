using SMDataManager.Library.Feeds;

namespace StockApi.Security
{
    // Picks the store for new secrets from configuration, but resolves existing ones using the
    // provider recorded on the feed row. That means switching environments (or migrating from
    // Data Protection to Key Vault) does not strand credentials written under the old store.
    public class FeedSecretStoreResolver : IFeedSecretStoreResolver
    {
        private readonly IEnumerable<IFeedSecretStore> _stores;
        private readonly string _activeProvider;

        public FeedSecretStoreResolver(IEnumerable<IFeedSecretStore> stores, IConfiguration config)
        {
            _stores = stores;
            _activeProvider = config["FeedSecrets:Provider"] ?? DataProtectionFeedSecretStore.Provider;
        }

        public IFeedSecretStore Active => For(_activeProvider);

        public IFeedSecretStore For(string providerName)
        {
            var wanted = string.IsNullOrWhiteSpace(providerName) ? _activeProvider : providerName;

            return _stores.FirstOrDefault(store =>
                       string.Equals(store.ProviderName, wanted, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException(
                       $"No feed secret store is registered for provider '{wanted}'. " +
                       "Check FeedSecrets:Provider in configuration.");
        }
    }
}
