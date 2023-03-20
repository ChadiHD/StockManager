using SMDesktopUI.Models;
using System.Net.Http;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Api
{
    public interface IAPIHelper
    {
        HttpClient ApiClient { get; }
        Task<AuthenticatedUser> Authenticate(string username, string password);
        void LogOff();
        Task GetLoggedInUserInfo(string token);
    }
}