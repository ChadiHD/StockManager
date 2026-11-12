using System.Data.Common;
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
/// The one thing it still does then is finish the configured admin's SMDatabase profile, if a
/// start before that schema existed left it without one (<see cref="Step.Finish"/>).
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
        Refuse,
        /// <summary>
        /// The configured login is an admin with no SMDatabase profile: an earlier start made it
        /// before that schema existed. It gains the profile and every store, as it would have then.
        /// </summary>
        Finish
    }

    public static (Step Step, string? Reason) Decide(
        bool anyAdmin, string? email, string? password, bool loginExists, bool unfinished = false)
    {
        var configured = !string.IsNullOrWhiteSpace(email);

        if (anyAdmin)
        {
            if (unfinished)
            {
                return (Step.Finish, null);
            }

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
        var profiles = scope.ServiceProvider.GetRequiredService<IUserData>();

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
        var login = StaffSignIn.IsStaffName(email) ? await users.FindByNameAsync(email!) : null;
        var unfinished = anyAdmin && login is not null
            && await users.IsInRoleAsync(login, AdminRole) && LacksProfile(profiles, login.Id, email!, logger);

        var (step, reason) = Decide(anyAdmin, email, password, login is not null, unfinished);

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

            case Step.Finish:
                if (WriteProfile(profiles, login!, email!, logger))
                {
                    logger.LogWarning(
                        "{Email} was made an admin before SMDatabase had its schema, and its profile is " +
                        "written now. Remove Admin:BootstrapEmail and Admin:BootstrapPassword from this " +
                        "deployment.", email);
                }
                return;
        }

        Check(await users.AddToRoleAsync(login!, AdminRole), $"make {email} an admin");

        if (WriteProfile(profiles, login!, email!, logger))
        {
            logger.LogWarning(
                "{Email} is now an admin. Remove Admin:BootstrapEmail and Admin:BootstrapPassword from " +
                "this deployment, and have them change the password from the portal.", email);
        }
    }

    // Only a profile known to be missing is finished. Finishing grants every store, so an admin
    // whose stores were since cut back must not be finished again because a read failed.
    private static bool LacksProfile(IUserData profiles, string userId, string email, ILogger logger)
    {
        try
        {
            return profiles.GetUserById(userId).Count == 0;
        }
        catch (DbException exception)
        {
            logger.LogError(exception,
                "SMDatabase could not be read, so {Email} may still be without its profile and unable to " +
                "sign in. Restart StockApi once the schema is published.", email);
            return false;
        }
    }

    // SMDatabase's half of an admin, written after ApiAuthDb's. A first deploy starts StockApi
    // before that schema is published, so this used to throw with the login and its role already
    // made, and every later start found an admin and stopped: /token issued a token, GET api/User
    // answered 404 for want of the profile, and the portal read that as a wrong password. Now it
    // logs, the host stays up, and the first start after the schema is published finishes it.
    private static bool WriteProfile(IUserData profiles, IdentityUser login, string email, ILogger logger)
    {
        try
        {
            if (profiles.GetUserById(login.Id).Count == 0)
            {
                profiles.CreateUser(new UserModel
                {
                    UserId = login.Id,
                    FirstName = "Administrator",
                    LastName = "",
                    EmailAddress = email
                });
            }

            // Every store, including ones not created yet: somebody has to be able to give every
            // other admin theirs, and on a fresh deployment there is nobody else (T9).
            profiles.SetSiteAccess(login.Id, allSites: true, Array.Empty<int>());

            return true;
        }
        catch (DbException exception)
        {
            logger.LogError(exception,
                "{Email} is an admin, but its profile could not be written to SMDatabase, so the portal " +
                "refuses its sign-in. Restart StockApi once the schema is published to finish it.", email);
            return false;
        }
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
