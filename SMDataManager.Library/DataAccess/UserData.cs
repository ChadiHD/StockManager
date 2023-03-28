using Microsoft.Extensions.Configuration;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDataManager.Library.DataAccess
{
    public class UserData
    {
        private readonly IConfiguration _config;

        public UserData(IConfiguration config)
        {
            _config = config;
        }
        public List<UserModel> GetUserById(string Id)
        {
            SqlDataAccess sql = new SqlDataAccess(_config);

            var p = new { UserId = Id };

            var output = sql.LoadData<UserModel, dynamic>("dbo.spUserLookup", p, "SMDatabase");

            return output;
        }
    }
}
