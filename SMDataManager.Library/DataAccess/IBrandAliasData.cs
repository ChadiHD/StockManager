using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IBrandAliasData
    {
        List<BrandAliasModel> GetAliases();
    }
}
