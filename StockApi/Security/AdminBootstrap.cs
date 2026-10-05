using Microsoft.AspNetCore.Identity;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;

namespace StockApi.Security;

/// <summary>
/// Gives a fresh deployment its first admin, and every deployment the staff roles.
/// </summary>
/// <remarks>
/// Every admin after the first is made by one who already exists, through the portal. The first
/// had no way in: <c>Register</c> is Admin-only, so standing up a deployment meant writing an
/// <c>AspNetUserRoles</c> row by hand. <c>Admin:BootstrapEmail</c> and
/// <c>Admin:BootstrapPassword</c> (secret parameters in the app host) close that, and only that:
/// once any user holds Admin this does nothing, so a value left configured cannot mint a second
/// admin or reset the first one's password. It says so in the log until the values are removed.
/// </remarks>
public static class AdminBootstrap
{
    public const string AdminRole = "Admin";

    /// <summary>The roles the portal's Manage roles dialog offers. A fresh store has none.</summary>
    public static readonly string[] StaffRoles = [AdminRole, "Manager", "Staff"];

    public enum Step
    {
        /// <summary>An admin exists and nothing is configured: the ordinary case.</summary>
        Nothing,
        /// <summary>An admin exists, so the configured values are ignored, and should go.</summary>
        IgnoreConfigured,
        /// <summary>No admin and nothing configured: nobody can sign in to the portal.</summary>
        WarnNoAdmin,
        Create,
        /// <summary>The login exists already: it gains Admin, and its password is left alone.</summary>
        Promote,
        Refuse
    }

    public static (Step Step, string? Reason) Decide(bool anyAdmin, string? email, string? password, bool loginExists)
    {
        var configured = !string.IsNullOrWhiteSpace(email);

        if (anyAdmin)
        {
            return (configured ? Step.IgnoreConfigured : Step.Nothing, null);
        }

        if (!configured)
        {
            return (Step.WarnNoAdmin, null);
        }

        if (!StaffSignIn.IsStaffName(email))
        {
            return (Step.Refuse, "Admin:BootstrapEmail is a customer-style login name; a staff login never contains '|'.");
        }

        if (loginExists)
        {
            return (Step.Promote, null);
        }

        return string.IsNullOrEmpty(password)
            ? (Step.Refuse, "Admin:BootstrapPassword is required to create the first admin.")
            : (Step.Create, null);
    }

    public static async Task EnsureAdminAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        using var scope = services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        foreach (var role in StaffRoles)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                Check(await roles.CreateAsync(new IdentityRole(role)), $"create the {role} role");
            }
        }

        var email = config["Admin:BootstrapEmail"]?.Trim();
        var password = config["Admin:BootstrapPassword"];
        var anyAdmin = (await users.GetUsersInRoleAsync(AdminRole)).Count > 0;
        var login = !anyAdmin && StaffSignIn.IsStaffName(email) ? await users.FindByNameAsync(email!) : null;

        var (step, reason) = Decide(anyAdmin, email, password, login is not null);

        switch (step)
        {
            case Step.Nothing:
                return;

            case Step.IgnoreConfigured:
                logger.LogWarning(
                    "Admin:BootstrapEmail is set but an admin already exists, so it is ignored. " +
                    "Remove Admin:BootstrapEmail and Admin:BootstrapPassword from this deployment.");
                return;

            case Step.WarnNoAdmin:
                logger.LogWarning(
                    "No user holds the Admin role, so nobody can sign in to the portal. Set " +
                    "Admin:BootstrapEmail and Admin:BootstrapPassword and restart to create one.");
                return;

            case Step.Refuse:
                throw new InvalidOperationException(reason);

            case Step.Create:
                login = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                Check(await users.CreateAsync(login, password!), $"create the first admin, {email}");
                break;

            case Step.Promote:
                break;
        }

        Check(await users.AddToRoleAsync(login!, AdminRole), $"make {email} an admin");

        // The portal finds its signed-in user's name here; without the row a good sign-in reads
        // as a failed one. See UserController.GetById.
        var profiles = scope.ServiceProvider.GetRequiredService<IUserData>();

        if (profiles.GetUserById(login!.Id).Count == 0)
        {
            profiles.CreateUser(new UserModel
            {
                UserId = login.Id,
                FirstName = "Administrator",
                LastName = "",
                EmailAddress = email!
            });
        }

        logger.LogWarning(
            "{Email} is now an admin. Remove Admin:BootstrapEmail and Admin:BootstrapPassword from " +
            "this deployment, and have them change the password from the portal.", email);
    }

    private static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {what}: {string.Join(" ", result.Errors.Select(error => error.Description))}");
        }
    }
}
