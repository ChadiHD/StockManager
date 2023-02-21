using SMDesktopUI.Models;
using System.Threading.Tasks;

namespace SMDesktopUI.Helpers
{
    public interface IAPIHelper
    {
        Task<AuthenticatedUser> Authenticate(string username, string password);
    }
}