using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// Distributor feed definitions, scoped to a store. See <see cref="IAccountData"/> for why
    /// siteId is mandatory and last on every method.
    /// </summary>
    /// <remarks>
    /// These rows carry SecretRef — ciphertext bound to the Data Protection ring StockApi
    /// holds — so an unscoped read here hands one tenant's admin another tenant's SFTP
    /// credentials from a host that can decrypt them. Of everything in this library it is the
    /// one where a missing site predicate does the most damage.
    /// </remarks>
    public interface IDistributorFeedData
    {
        List<DistributorFeedModel> GetFeeds(int siteId);
        DistributorFeedModel GetFeedById(int id, int siteId);
        DistributorFeedModel CreateFeed(DistributorFeedModel feed, int siteId);
        void UpdateFeed(DistributorFeedModel feed, int siteId);

        /// <summary>Changes only the stored credential reference.</summary>
        void UpdateSecret(int id, string secretProvider, string secretRef, int siteId);

        void DeleteFeed(int id, int siteId);

        /// <summary>
        /// Takes the right to sync this feed, or returns false because something else holds it.
        /// </summary>
        /// <remarks>
        /// False is not an error. It means a sync of this feed is already running — an operator
        /// pressing Sync inside the nightly window, or a second host whose scheduler woke at
        /// the same hour. The claim expires on a lease held in the procedure, so a process
        /// killed mid-sync does not take the feed out of service permanently.
        ///
        /// <see cref="RecordSync"/> releases it, which is why every path that claims must reach
        /// one.
        /// </remarks>
        bool ClaimForSync(int id, int siteId);

        /// <summary>
        /// Records the outcome, writes it to the history, and releases the claim.
        /// </summary>
        /// <remarks>
        /// One call for all three because a caller that did two of them would leave a state
        /// nobody reads correctly: a status without a release looks like a sync that finished
        /// and then would not start again, and a release without a history row loses what the
        /// failure alert uses to tell a new failure from a continuing one.
        /// </remarks>
        void RecordSync(int id, int siteId, FeedSyncRecord record);

        /// <summary>Recent attempts against one feed, newest first.</summary>
        List<FeedSyncLogModel> GetSyncHistory(int feedId, int siteId, int take);

        /// <summary>Recent attempts across every feed in one store, newest first.</summary>
        List<FeedSyncLogModel> GetRecentSyncs(int siteId, int take);

        /// <summary>
        /// Enabled feeds this store has not heard from within its own staleness threshold.
        /// </summary>
        /// <remarks>
        /// A different question from "did the last sync fail". A feed that was never attempted
        /// — the scheduler off, the host down, a misconfigured time — leaves nothing in the
        /// history to fail, and this is the only thing that notices. Empty when the store has
        /// set no threshold.
        /// </remarks>
        List<DistributorFeedModel> GetStaleFeeds(int siteId);
    }
}
