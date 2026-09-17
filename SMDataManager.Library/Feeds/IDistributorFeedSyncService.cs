using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SMDataManager.Library.Feeds
{
    public interface IDistributorFeedSyncService
    {
        /// <summary>
        /// Pulls every enabled feed for one store and upserts the products it contains.
        /// Scoped rather than global: a feed belongs to a store, and the scheduled sync loops
        /// over active sites calling this once each.
        /// </summary>
        /// <param name="trigger">
        /// Recorded against every attempt this run makes. Passed in rather than inferred: a
        /// method call cannot tell a nightly run from a button, and a feed that only ever
        /// succeeds when somebody presses the button is a scheduler problem rather than a feed
        /// problem. No default, so a new caller has to decide which it is.
        /// </param>
        Task<List<DistributorFeedResult>> SyncAllAsync(int siteId, FeedSyncTrigger trigger);

        /// <summary>Pulls a single feed by id, within one store.</summary>
        Task<DistributorFeedResult> SyncAsync(int feedId, int siteId, FeedSyncTrigger trigger);

        /// <summary>
        /// Connects and parses without importing, so a feed can be verified before it is saved.
        /// Pass the password for a new feed, or leave it empty to use the stored credential.
        /// </summary>
        Task<DistributorFeedResult> TestAsync(DistributorFeedModel feed, string password);
    }
}
