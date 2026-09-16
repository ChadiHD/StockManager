using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SMStore.Accounts;
using StockManager.Identity;

namespace StockManager.E2ETests.Infrastructure;

/// <summary>
/// A throwaway Identity container against ApiAuthDb, for provisioning the two kinds of login
/// these journeys need but cannot obtain through the product itself.
/// </summary>
/// <remarks>
/// There is no supported way to create the first admin through the running system:
/// POST /api/User/Admin/AddRole is [Authorize(Roles = "Admin")], and the only anonymous
/// registration endpoint, POST /api/User/Register, grants no role at all (see
/// StockApi/Controllers/UserController.cs). Every admin after the first is created by one who
/// already exists, so bootstrapping the very first is the one seam a test has to reach around
/// the front door for.
///
/// This talks to the same ApiAuthDb through the same ApplicationDbContext and
/// UserManager/RoleManager the real hosts use, rather than hand-writing AspNetUsers rows, so a
/// schema change or a password-hashing change here breaks in exactly the way it would break
/// the product, rather than silently drifting out of step with it.
/// </remarks>
public static class IdentityTestSupport
{
    // Framework-default Identity password rules (a digit, upper and lower case, a
    // non-alphanumeric, six characters minimum) apply here because nothing below configures
    // its own -- StockApi's Program.cs does not configure PasswordOptions either, so this is
    // what a real registration or admin action would accept too.
    private const string Password = "E2e-Test-Passw0rd!";

    public sealed record Credentials(string Email, string Password);

    /// <summary>The one admin every admin-facing journey in this project signs in as.</summary>
    public static async Task<Credentials> CreateAdminAsync(
        string apiAuthConnectionString,
        string smDatabaseConnectionString,
        CancellationToken cancellationToken = default)
    {
        var email = $"e2e-admin-{Guid.NewGuid():N}@example.test";

        await using var provider = BuildProvider(apiAuthConnectionString);
        await using var scope = provider.CreateAsyncScope();

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin"))
        {
            Guard(await roles.CreateAsync(new IdentityRole("Admin")), "create the Admin role");
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        // TokenController.IsValidUsernameAndPassword looks a caller up by
        // UserManager.FindByEmailAsync, not by UserName -- unlike a customer login, an admin's
        // login is not site-qualified, so UserName and Email can be the same value here.
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };

        Guard(await users.CreateAsync(user, Password), $"create admin user {email}");
        Guard(await users.AddToRoleAsync(user, "Admin"), $"grant Admin to {email}");

        // An admin exists in both databases, and the profile row is the half that is easy to
        // forget: without it the token is issued, GET /api/User answers 404, and SMPortal
        // reports a perfectly good sign-in as a wrong password. See CreateUserProfileAsync.
        await SqlTestData.CreateUserProfileAsync(
            smDatabaseConnectionString, user.Id, email, cancellationToken);

        return new Credentials(email, Password);
    }

    /// <summary>
    /// A customer login and its already-approved trading account, written directly rather
    /// than through /register and an admin decision.
    /// </summary>
    /// <remarks>
    /// Used only by CrossTenantRefusalJourneyTests, which tests what happens to a session once
    /// one exists -- RegistrationApprovalJourneyTests is what exercises the real form and the
    /// real approval screen, and this would only be that journey again under a different name.
    /// </remarks>
    public static async Task<Credentials> CreateApprovedCustomerAsync(
        string apiAuthConnectionString,
        string smDatabaseConnectionString,
        string siteKey,
        int siteId,
        CancellationToken cancellationToken = default)
    {
        var email = $"e2e-customer-{Guid.NewGuid():N}@example.test";
        var userName = SiteQualifiedUserName.For(siteKey, email);

        await using var provider = BuildProvider(apiAuthConnectionString);
        await using var scope = provider.CreateAsyncScope();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser { UserName = userName, Email = email, EmailConfirmed = true };

        Guard(await users.CreateAsync(user, Password), $"create customer login {userName}");

        await SqlTestData.CreateApprovedAccountAndContactAsync(
            smDatabaseConnectionString, siteId, user.Id, email, cancellationToken);

        return new Credentials(email, Password);
    }

    /// <summary>
    /// A real confirmation link for an address that has just registered, so a journey can
    /// follow the one the applicant was mailed.
    /// </summary>
    /// <remarks>
    /// The alternative — setting <c>EmailConfirmed</c> directly — would skip the whole feature
    /// the journey is meant to prove, and sign-in now refuses an unconfirmed address, so
    /// something has to stand in for opening the mail. The token is generated through the same
    /// provider and the same key ring the storefront validates against; see BuildProvider.
    /// </remarks>
    public static async Task<string> ConfirmationUrlAsync(
        string apiAuthConnectionString,
        Uri storeBaseUrl,
        string siteKey,
        string email,
        CancellationToken cancellationToken = default)
    {
        await using var provider = BuildProvider(apiAuthConnectionString);
        await using var scope = provider.CreateAsyncScope();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var userName = SiteQualifiedUserName.For(siteKey, email);

        var user = await users.FindByNameAsync(userName)
            ?? throw new InvalidOperationException(
                $"No login named {userName} exists, so no confirmation link can be built for it.");

        var token = await users.GenerateEmailConfirmationTokenAsync(user);

        // Built against the store's base URL rather than Site.Domain: under the testing builder
        // the storefront answers on localhost, and EmailConfirmationLink.For deliberately uses
        // the configured domain, which nothing in a test run is listening on.
        return new Uri(storeBaseUrl,
            $"/confirm-email?userId={Uri.EscapeDataString(user.Id)}" +
            $"&token={EmailConfirmationLink.Encode(token)}").ToString();
    }

    private static ServiceProvider BuildProvider(string apiAuthConnectionString)
    {
        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(apiAuthConnectionString));

        /*
        The same Data Protection key ring the hosts use, which is what makes the tokens this
        container mints acceptable to SMStore.

        Identity's email-confirmation tokens come from DataProtectorTokenProvider, so they are
        only valid to a process holding the key that wrote them. With an ephemeral ring here,
        a token generated in the test would be refused by the storefront and the confirmation
        journey could only be faked by setting EmailConfirmed directly — which would test
        nothing. The application name has to match AddSharedDataProtection's, because Data
        Protection derives per-application subkeys from it.
        */
        services.AddDbContext<DataProtectionKeyContext>(options => options.UseSqlServer(apiAuthConnectionString));
        services.AddDataProtection()
            .SetApplicationName("StockManager")
            .PersistKeysToDbContext<DataProtectionKeyContext>();

        // AddIdentityCore rather than AddDefaultIdentity: there is no web host here, so this
        // wants UserManager and RoleManager and nothing that assumes a request pipeline.
        services.AddIdentityCore<IdentityUser>(options =>
            {
                /*
                The same two options StockApi and SMStore both set, and they are not optional
                here either.

                A customer login is named "{SiteKey}|{email}", and Identity's default
                allow-list rejects the pipe — this container refused to create one at all
                ("can only contain letters or digits") until it agreed with the hosts. That it
                failed is the rule working: a test that quietly used a different username
                policy would be provisioning logins the product could never have made.
                */
                options.User.AllowedUserNameCharacters = SiteQualifiedUserName.AllowedUserNameCharacters;
                options.User.RequireUniqueEmail = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            // The email-confirmation token provider, so this container can mint the link a
            // journey follows. SMStore adds the same providers.
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }

    private static void Guard(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {action}: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }
    }
}
