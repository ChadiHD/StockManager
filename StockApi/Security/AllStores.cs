using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SMDataManager.Library.DataAccess;

namespace StockApi.Security;

/// <summary>
/// The authorization policy for actions that are not about one store: staff and their store
/// grants, and the till's own data. An admin limited to some stores may not use them (T9).
/// </summary>
/// <remarks>
/// It binds admins only. Manager and Staff are the desktop till's roles, and the till has no
/// store at all, so for a caller who is not an Admin this requirement passes and the action's
/// own roles decide, exactly as before. For an Admin it demands <c>dbo.[User].AllSites</c>,
/// read per request for the reason store access is: a token lives a day.
///
/// Pair it with the roles the action admits; it never grants anything on its own.
/// </remarks>
public static class AllStores
{
    public const string Policy = "AllStores";

    public sealed class Requirement : IAuthorizationRequirement
    {
    }

    public sealed class Handler : AuthorizationHandler<Requirement>
    {
        private readonly IUserData _users;

        public Handler(IUserData users) => _users = users;

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, Requirement requirement)
        {
            if (!context.User.IsInRole(AdminBootstrap.AdminRole) || ManagesEveryStore(context.User))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }

        private bool ManagesEveryStore(ClaimsPrincipal user)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

            return !string.IsNullOrEmpty(userId)
                && _users.GetUserById(userId).FirstOrDefault()?.AllSites == true;
        }
    }
}
