using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public class BrandAliasData : IBrandAliasData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public BrandAliasData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<BrandAliasModel> GetAliases()
        {
            return _sqlDataAccess.LoadData<BrandAliasModel, dynamic>(
                "dbo.spBrandAlias_GetAll", new { }, "SMDatabase");
        }
    }
}
