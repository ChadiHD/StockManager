using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SMDataManager.Library.Feeds
{
    public interface IDistributorFeedSyncService
    {
        /// <summary>
        /// Pulls every enabled feed for one store and upserts the products it contains.
        /// Scoped rather than global: a feed belongs to a store, and T4's scheduled sync is
        /// expected to loop over active sites calling this once each.
        /// </summary>
        Task<List<DistributorFeedResult>> SyncAllAsync(int siteId);

        /// <summary>Pulls a single feed by id, within one store.</summary>
        Task<DistributorFeedResult> SyncAsync(int feedId, int siteId);

        /// <summary>
        /// Connects and parses without importing, so a feed can be verified before it is saved.
        /// Pass the password for a new feed, or leave it empty to use the stored credential.
        /// </summary>
        Task<DistributorFeedResult> TestAsync(DistributorFeedModel feed, string password);
    }
}
