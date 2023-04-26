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
    public class UserData : IUserData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public UserData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }
        public List<UserModel> GetUserById(string UserId)
        {
            var output = _sqlDataAccess.LoadData<UserModel, dynamic>("dbo.spUserLookup", new { UserId }, "SMDatabase");

            return output;
        }

        public void CreateUser(UserModel user)
        {
            _sqlDataAccess.SaveData("dbo.spUser_Insert", new {user.UserId, user.FirstName, user.LastName, user.EmailAddress}, "SMDatabase");
        }
    }
}
