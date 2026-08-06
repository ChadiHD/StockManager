using System.Threading;
using System.Threading.Tasks;

namespace SMDataManager.Library.Feeds
{
    // Keeps distributor passwords out of the database and out of configuration.
    //
    // A store turns a plaintext secret into a reference that is safe to persist, and resolves
    // it back when a sync runs. Two implementations live in the API: Data Protection (the
    // reference IS the ciphertext) and Key Vault (the reference is the secret's name, and the
    // value never leaves the vault's control). Which one is active is an appsettings choice per
    // environment, so development and production can differ without any code change.
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
