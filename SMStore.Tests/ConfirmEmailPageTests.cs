using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using SMStore.Accounts;
using SMStore.Tests.TestSupport;
using Xunit;
using ConfirmEmailPage = SMStore.Components.Pages.ConfirmEmail;

namespace SMStore.Tests;

public class ConfirmEmailPageTests
{
    private const string EncodedToken = "Y29uZmlybS10b2tlbg"; // base64url of "confirm-token"

    [Fact]
    public async Task FollowingTheLinkConfirmsNothingUntilTheButtonIsPressed()
    {
        // Mail gateways open every link in a message to scan it. Confirming on arrival let a
        // scanner confirm an address an applicant had typed in for somebody else.
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();
        harness.Users.ConfirmEmailAsync(user, Arg.Any<string>()).Returns(IdentityResult.Success);

        using var context = new Bunit.TestContext();
        context.Services.AddSingleton(harness.SiteContext);
        context.Services.AddSingleton(harness.Users);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(
            $"{CustomerAuthentication.ConfirmEmailPath}?{MailedTokenLink.UserIdParameter}={user.Id}" +
            $"&{MailedTokenLink.TokenParameter}={EncodedToken}");

        var posted = new DefaultHttpContext
        {
            Request =
            {
                Form = new FormCollection(new Dictionary<string, StringValues>
                {
                    [MailedTokenLink.UserIdParameter] = user.Id,
                    [MailedTokenLink.TokenParameter] = EncodedToken
                })
            }
        };

        var cut = context.RenderComponent<ConfirmEmailPage>(parameters => parameters.AddCascadingValue<HttpContext>(posted));

        cut.Find("h1").TextContent.Should().Be("Confirm your email address");
        await harness.Users.DidNotReceiveWithAnyArgs().ConfirmEmailAsync(default!, default!);

        cut.Find("form").Submit();

        cut.Find("h1").TextContent.Should().Be("Address confirmed");
        await harness.Users.Received(1).ConfirmEmailAsync(user, "confirm-token");
    }
}
