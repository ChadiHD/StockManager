using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class AddressData : IAddressData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public AddressData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<AddressModel> GetByAccount(int accountId, int siteId)
        {
            return _sqlDataAccess.LoadData<AddressModel, dynamic>(
                "dbo.spAddress_GetByAccount",
                new { AccountId = accountId, SiteId = siteId }, "SMDatabase");
        }

        public AddressModel Insert(AddressModel address, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spAddress_Insert", new
            {
                Id = 0,
                address.AccountId,
                address.Kind,
                address.Line1,
                address.Line2,
                address.City,
                address.Region,
                address.PostCode,
                address.Country,
                address.IsDefault,
                SiteId = siteId
            }, "SMDatabase");

            // Newest of its kind: the procedure may have promoted this one to default, so
            // matching on IsDefault would be ambiguous where matching on the key is not.
            return GetByAccount(address.AccountId, siteId)
                .Where(candidate => candidate.Kind == address.Kind)
                .OrderByDescending(candidate => candidate.Id)
                .FirstOrDefault();
        }

        public void Update(AddressModel address, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spAddress_Update", new
            {
                address.Id,
                address.Line1,
                address.Line2,
                address.City,
                address.Region,
                address.PostCode,
                address.Country,
                address.IsDefault,
                SiteId = siteId
            }, "SMDatabase");
        }

        public bool Delete(int id, int siteId)
        {
            return _sqlDataAccess.LoadData<int, dynamic>(
                "dbo.spAddress_Delete", new { Id = id, SiteId = siteId },
                "SMDatabase").FirstOrDefault() > 0;
        }
    }
}
