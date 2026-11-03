using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using StockApi.Security;
using StockApi.Tests.TestSupport;
using Xunit;

namespace StockApi.Tests.Security;

// Until the stamp was checked, removing an admin's role left their token working for the rest
// of its day, and a token is all this host looks at. None of these fail loudly if the check
// goes: every token simply keeps working.
public class StaffTokenTests
{
    private const string SigningKey = "staff-token-test-signing-key-0123456789abcdef";

    [Fact]
    public async Task ATokenIsAcceptedWhileItsLoginIsUnchanged()
    {
        var login = new IdentityUser("admin@example.test") { SecurityStamp = "stamp-1" };
        var users = Users(login);

        var principal = await IssueAndReadAsync(users, login);

        (await StaffToken.IsCurrentAsync(principal, users)).Should().BeTrue();
    }

    [Fact]
    public async Task ATokenIsRefusedOnceTheStampRotates()
    {
        // What RemoveRole and a password change both do to the login.
        var login = new IdentityUser("admin@example.test") { SecurityStamp = "stamp-1" };
        var users = Users(login);
        var principal = await IssueAndReadAsync(users, login);

        login.SecurityStamp = "stamp-2";

        (await StaffToken.IsCurrentAsync(principal, users)).Should().BeFalse();
    }

    [Fact]
    public async Task ATokenForALoginThatNoLongerExistsIsRefused()
    {
        var login = new IdentityUser("admin@example.test") { SecurityStamp = "stamp-1" };
        var principal = await IssueAndReadAsync(Users(login), login);

        (await StaffToken.IsCurrentAsync(principal, Users())).Should().BeFalse();
    }

    [Fact]
    public async Task ATokenIssuedBeforeTheCheckExistedIsRefused()
    {
        // No stamp claim at all. Skipping the comparison here would leave the old tokens, and
        // anything minted the old way, outside the check.
        var login = new IdentityUser("admin@example.test") { SecurityStamp = "stamp-1" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, login.Id)], "Bearer"));

        (await StaffToken.IsCurrentAsync(principal, Users(login))).Should().BeFalse();
    }

    [Fact]
    public async Task ProgramWiresTheCheckIntoBearerValidation()
    {
        // The check above is worthless if nothing calls it, and nothing else would notice.
        using var host = ProgramHost.Build();
        var options = host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        using var scope = host.Services.CreateScope();

        var context = new TokenValidatedContext(
            new DefaultHttpContext { RequestServices = scope.ServiceProvider },
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "someone")], "Bearer"))
        };

        await options.Events.OnTokenValidated(context);

        (context.Result?.Failure).Should().NotBeNull("a token without the stamp claim must not authenticate");
    }

    // Issued and read back the way the host does, so the claim's name survives the JWT's own
    // claim-type mapping in both directions.
    private static async Task<ClaimsPrincipal> IssueAndReadAsync(UserManager<IdentityUser> users, IdentityUser login)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = SigningKey })
            .Build();

        var issued = await StaffToken.IssueAsync(users, configuration, login);
        var token = JsonSerializer.SerializeToElement(issued).GetProperty("Access_Token").GetString();

        return new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidIssuer = StaffToken.Issuer,
            ValidAudience = StaffToken.Audience
        }, out _);
    }

    private static UserManager<IdentityUser> Users(params IdentityUser[] logins)
    {
        var users = Substitute.For<UserManager<IdentityUser>>(
            Substitute.For<IUserStore<IdentityUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        users.FindByIdAsync(Arg.Any<string>())
            .Returns(call => logins.FirstOrDefault(login => login.Id == call.Arg<string>()));
        users.GetRolesAsync(Arg.Any<IdentityUser>()).Returns(["Admin"]);

        return users;
    }
}
