using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class DistributorFeedData : IDistributorFeedData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public DistributorFeedData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<DistributorFeedModel> GetFeeds(int siteId)
        {
            return _sqlDataAccess.LoadData<DistributorFeedModel, dynamic>(
                "dbo.spDistributorFeed_GetAll", new { SiteId = siteId }, "SMDatabase");
        }

        public DistributorFeedModel GetFeedById(int id, int siteId)
        {
            return _sqlDataAccess.LoadData<DistributorFeedModel, dynamic>(
                "dbo.spDistributorFeed_GetById", new { Id = id, SiteId = siteId },
                "SMDatabase").FirstOrDefault();
        }

        public DistributorFeedModel CreateFeed(DistributorFeedModel feed, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spDistributorFeed_Insert", new
            {
                Id = 0,
                feed.Name,
                feed.Host,
                Port = feed.Port <= 0 ? 22 : feed.Port,
                feed.Username,
                SecretProvider = feed.SecretProvider,
                SecretRef = feed.SecretRef,
                RemoteDirectory = string.IsNullOrWhiteSpace(feed.RemoteDirectory) ? "." : feed.RemoteDirectory,
                feed.HostKeySha256,
                feed.Enabled,
                feed.FieldSku,
                feed.FieldName,
                feed.FieldDescription,
                feed.FieldCategory,
                feed.FieldCost,
                feed.FieldSrp,
                feed.FieldQuantity,
                feed.FieldManufacturer,
                feed.FieldMpn,
                feed.FieldEan,
                feed.FieldIcecat,
                SiteId = siteId
            }, "SMDatabase");

            return GetFeeds(siteId).FirstOrDefault(candidate => candidate.Name == feed.Name);
        }

        public void UpdateFeed(DistributorFeedModel feed, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spDistributorFeed_Update", new
            {
                feed.Id,
                feed.Name,
                feed.Host,
                Port = feed.Port <= 0 ? 22 : feed.Port,
                feed.Username,
                RemoteDirectory = string.IsNullOrWhiteSpace(feed.RemoteDirectory) ? "." : feed.RemoteDirectory,
                feed.HostKeySha256,
                feed.Enabled,
                feed.FieldSku,
                feed.FieldName,
                feed.FieldDescription,
                feed.FieldCategory,
                feed.FieldCost,
                feed.FieldSrp,
                feed.FieldQuantity,
                feed.FieldManufacturer,
                feed.FieldMpn,
                feed.FieldEan,
                feed.FieldIcecat,
                SiteId = siteId
            }, "SMDatabase");
        }

        public void UpdateSecret(int id, string secretProvider, string secretRef, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spDistributorFeed_UpdateSecret", new
            {
                Id = id,
                SecretProvider = secretProvider,
                SecretRef = secretRef,
                SiteId = siteId
            }, "SMDatabase");
        }

        public void DeleteFeed(int id, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spDistributorFeed_Delete",
                new { Id = id, SiteId = siteId }, "SMDatabase");
        }

        public bool ClaimForSync(int id, int siteId)
        {
            // LoadData rather than SaveData: the procedure SELECTs whether the claim was taken,
            // and SaveData passes an anonymous object Dapper cannot write an output parameter
            // back through. See CLAUDE.md on returning a value from a mutation.
            var claimed = _sqlDataAccess.LoadData<int, dynamic>(
                "dbo.spDistributorFeed_ClaimForSync",
                new { Id = id, SiteId = siteId }, "SMDatabase");

            return claimed.FirstOrDefault() == 1;
        }

        public void RecordSync(int id, int siteId, FeedSyncRecord record)
        {
            _sqlDataAccess.SaveData("dbo.spDistributorFeed_RecordSync", new
            {
                Id = id,
                SiteId = siteId,
                Status = record.Status,
                Succeeded = record.Succeeded,
                StartedUtc = record.StartedUtc,
                RecordCount = record.RecordCount,
                Imported = record.Imported,
                Delisted = record.Delisted,
                // The enum's name is the stored value, so adding a trigger is one place rather
                // than two. The column is NVARCHAR(20) and both names fit.
                TriggeredBy = record.TriggeredBy.ToString()
            }, "SMDatabase");
        }

        public List<FeedSyncLogModel> GetSyncHistory(int feedId, int siteId, int take)
        {
            return _sqlDataAccess.LoadData<FeedSyncLogModel, dynamic>(
                "dbo.spDistributorFeedSync_GetByFeed",
                new { FeedId = feedId, SiteId = siteId, Take = take }, "SMDatabase");
        }

        public List<FeedSyncLogModel> GetRecentSyncs(int siteId, int take)
        {
            return _sqlDataAccess.LoadData<FeedSyncLogModel, dynamic>(
                "dbo.spDistributorFeedSync_GetRecent",
                new { SiteId = siteId, Take = take }, "SMDatabase");
        }
    }
}
