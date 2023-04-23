using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IUserData
    {
        void CreateUser(UserModel user);
        List<UserModel> GetUserById(string Id);
    }
}