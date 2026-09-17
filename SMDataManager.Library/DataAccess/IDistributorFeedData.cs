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
        void RecordSync(int id, string status, int siteId);
    }
}
