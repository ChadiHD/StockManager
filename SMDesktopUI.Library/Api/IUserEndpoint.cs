using SMDesktopUI.Library.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Api
{
    public interface IUserEndpoint
    {
        Task<List<UserModel>> GetAll();
    }
}