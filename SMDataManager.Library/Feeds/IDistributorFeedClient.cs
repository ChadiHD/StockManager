using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.Feeds
{
    // Fetches and parses a distributor's stock feed. Implemented over SFTP today; swapping in
    // an HTTP or API-based distributor means a new implementation, not a change to callers.
    public interface IDistributorFeedClient
    {
        IReadOnlyList<DistributorFeedRecord> Fetch(DistributorFeedSettings settings);
    }
}
