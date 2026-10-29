using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public class ShippingRateData : IShippingRateData
    {
        /// <summary>The number spShippingRate_Save THROWs for another store's rate, or none.</summary>
        private const int NoSuchRate = 50141;

        private readonly ISqlDataAccess _sqlDataAccess;

        public ShippingRateData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<ShippingRateModel> GetBySite(int siteId) =>
            _sqlDataAccess.LoadData<ShippingRateModel, dynamic>(
                "dbo.spShippingRate_GetBySite", new { SiteId = siteId }, "SMDatabase");

        public bool Save(ShippingRateModel rate, int siteId)
        {
            try
            {
                _sqlDataAccess.SaveData("dbo.spShippingRate_Save", new
                {
                    SiteId = siteId,
                    Id = rate.Id == 0 ? (int?)null : rate.Id,
                    rate.Name,
                    rate.Countries,
                    rate.Price,
                    rate.FreeOver,
                    rate.IsActive
                }, "SMDatabase");

                return true;
            }
            catch (SqlException ex) when (ex.Number == NoSuchRate)
            {
                return false;
            }
        }
    }
}
