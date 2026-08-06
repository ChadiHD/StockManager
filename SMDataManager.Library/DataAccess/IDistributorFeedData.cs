using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IDistributorFeedData
    {
        List<DistributorFeedModel> GetFeeds();
        DistributorFeedModel GetFeedById(int id);
        DistributorFeedModel CreateFeed(DistributorFeedModel feed);
        void UpdateFeed(DistributorFeedModel feed);

        /// <summary>Changes only the stored credential reference.</summary>
        void UpdateSecret(int id, string secretProvider, string secretRef);

        void DeleteFeed(int id);
        void RecordSync(int id, string status);
    }
}
