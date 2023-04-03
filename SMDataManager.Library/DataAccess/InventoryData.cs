using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDataManager.Library.DataAccess
{
    public class InventoryData : IInventoryData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public InventoryData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }
        public List<InventoryModel> GetInventory()
        {
            var output = _sqlDataAccess.LoadData<InventoryModel, dynamic>("dbo.spInventory_GetAll", new { }, "SMDatabase");

            return output;
        }

        public void SaveInventoryData(InventoryModel item)
        {
            _sqlDataAccess.SaveData("dbo.spInventory_Insert", item, "SMDatabase");
        }
    }
}
