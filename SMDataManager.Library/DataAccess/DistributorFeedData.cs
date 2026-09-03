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

        public List<DistributorFeedModel> GetFeeds()
        {
            return _sqlDataAccess.LoadData<DistributorFeedModel, dynamic>(
                "dbo.spDistributorFeed_GetAll", new { }, "SMDatabase");
        }

        public DistributorFeedModel GetFeedById(int id)
        {
            return _sqlDataAccess.LoadData<DistributorFeedModel, dynamic>(
                "dbo.spDistributorFeed_GetById", new { Id = id }, "SMDatabase").FirstOrDefault();
        }

        public DistributorFeedModel CreateFeed(DistributorFeedModel feed)
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
                feed.FieldIcecat
            }, "SMDatabase");

            return GetFeeds().FirstOrDefault(candidate => candidate.Name == feed.Name);
        }

        public void UpdateFeed(DistributorFeedModel feed)
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
                feed.FieldIcecat
            }, "SMDatabase");
        }

        public void UpdateSecret(int id, string secretProvider, string secretRef)
        {
            _sqlDataAccess.SaveData("dbo.spDistributorFeed_UpdateSecret", new
            {
                Id = id,
                SecretProvider = secretProvider,
                SecretRef = secretRef
            }, "SMDatabase");
        }

        public void DeleteFeed(int id)
        {
            _sqlDataAccess.SaveData("dbo.spDistributorFeed_Delete", new { Id = id }, "SMDatabase");
        }

        public void RecordSync(int id, string status)
        {
            _sqlDataAccess.SaveData("dbo.spDistributorFeed_RecordSync",
                new { Id = id, Status = status }, "SMDatabase");
        }
    }
}
