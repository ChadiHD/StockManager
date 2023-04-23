using SMPortal.Models;

namespace SMPortal.Authentication
{
    public interface IAuthenticationService
    {
        Task<AuthenticatedUserModel> Login(AuthenticationUserModel userForAuthentication);
        Task Logout();
    }
}