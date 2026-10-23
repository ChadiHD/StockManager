using System.Threading;
using System.Threading.Tasks;

namespace SMDataManager.Library.Feeds
{
    // Keeps distributor passwords out of the database and out of configuration.
    //
    // A store turns a plaintext secret into a reference that is safe to persist, and resolves
    // it back when a sync runs. The API's one implementation is Data Protection: the reference
    // IS the ciphertext, and a deployment wraps the key ring with its vault key. A Key Vault
    // store existed until template v1.9 and was removed: no deployment ever wired it, and it
    // named each secret after the feed alone, so two stores' feeds of one name shared a
    // password. A second store must keep its references apart per site.
    public interface IFeedSecretStore
    {
        /// <summary>Name recorded on the feed row so the right store resolves it later.</summary>
        string ProviderName { get; }

        /// <summary>Persists the secret and returns the reference to store on the feed.</summary>
        Task<string> ProtectAsync(string feedName, string secret, CancellationToken cancellationToken = default);

        /// <summary>Resolves a stored reference back to the usable secret.</summary>
        Task<string> ResolveAsync(string reference, CancellationToken cancellationToken = default);

        /// <summary>Best-effort cleanup when a feed is deleted.</summary>
        Task RemoveAsync(string reference, CancellationToken cancellationToken = default);
    }

    // Resolves the store that matches a feed row's recorded provider, so rows written under a
    // previous configuration keep working after the environment switches stores.
    public interface IFeedSecretStoreResolver
    {
        IFeedSecretStore Active { get; }
        IFeedSecretStore For(string providerName);
    }
}
