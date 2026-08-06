using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SMDataManager.Library.Feeds
{
    public interface IDistributorFeedSyncService
    {
        /// <summary>Pulls every enabled feed and upserts the products it contains.</summary>
        Task<List<DistributorFeedResult>> SyncAllAsync();

        /// <summary>Pulls a single feed by id.</summary>
        Task<DistributorFeedResult> SyncAsync(int feedId);

        /// <summary>
        /// Connects and parses without importing, so a feed can be verified before it is saved.
        /// Pass the password for a new feed, or leave it empty to use the stored credential.
        /// </summary>
        Task<DistributorFeedResult> TestAsync(DistributorFeedModel feed, string password);
    }
}
