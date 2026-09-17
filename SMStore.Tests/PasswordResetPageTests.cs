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
using ForgotPasswordPage = SMStore.Components.Pages.ForgotPassword;
using ResetPasswordPage = SMStore.Components.Pages.ResetPassword;

namespace SMStore.Tests;

/// <summary>
/// The two pages password reset renders, and the promises their markup keeps.
/// </summary>
/// <remarks>
/// Two of these are guards rather than behaviour checks. The reset form must never carry a
/// posted password back into its own HTML, and the forgot-password page must offer the
/// confirmation route to everybody rather than only to the people who need it — showing it
/// selectively would be the disclosure the neutral answer exists to prevent. Neither is
/// expressible as an ordinary assertion, so both are asserted against the rendered markup.
/// </remarks>
public class PasswordResetPageTests
{
    private const string EncodedToken = "cmVzZXQtdG9rZW4"; // base64url of "reset-token"

    private static HttpContext FormPost(Dictionary<string, StringValues> fields) => new DefaultHttpContext
    {
        Request = { Form = new FormCollection(fields, new FormFileCollection()) }
    };

    /// <summary>
    /// Puts the page's query string where it really comes from.
    /// </summary>
    /// <remarks>
    /// bUnit refuses a <c>[SupplyParameterFromQuery]</c> parameter passed as a parameter —
    /// "use the NavigationManager and navigate to the URI" — which is the right refusal: these
    /// values arrive by URL in production, and setting them directly would skip the binding
    /// that turns <c>?page=abc</c> into a 500 on pages that bind a number.
    /// </remarks>
    private static void NavigateTo(Bunit.TestContext context, string path, params (string, string?)[] query)
    {
        var parameters = query
            .Where(pair => pair.Item2 is not null)
            .Select(pair => $"{pair.Item1}={Uri.EscapeDataString(pair.Item2!)}")
            .ToArray();

        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo(parameters.Length == 0 ? path : $"{path}?{string.Join('&', parameters)}");
    }

    private static IRenderedComponent<ForgotPasswordPage> RenderForgotPassword(
        Bunit.TestContext context, PasswordResetHarness harness, string? sent = null)
    {
        context.Services.AddSingleton(harness.SiteContext);

        NavigateTo(context, CustomerAuthentication.ForgotPasswordPath, ("sent", sent));

        return context.RenderComponent<ForgotPasswordPage>();
    }

    private static IRenderedComponent<ResetPasswordPage> RenderResetPassword(
        Bunit.TestContext context,
        PasswordResetHarness harness,
        string? userId = null,
        string? token = null,
        HttpContext? httpContext = null)
    {
        context.Services.AddSingleton(harness.SiteContext);
        context.Services.AddSingleton(harness.Service);

        NavigateTo(context, CustomerAuthentication.ResetPasswordPath,
            (MailedTokenLink.UserIdParameter, userId),
            (MailedTokenLink.TokenParameter, token));

        return httpContext is null
            ? context.RenderComponent<ResetPasswordPage>()
            : context.RenderComponent<ResetPasswordPage>(
                parameters => parameters.AddCascadingValue(httpContext));
    }

    [Fact]
    public void TheRequestFormPostsToTheEndpointRatherThanBackToItself()
    {
        var harness = new PasswordResetHarness();
        using var context = new Bunit.TestContext();

        var cut = RenderForgotPassword(context, harness);

        cut.Find("form").GetAttribute("action")
            .Should().Be(CustomerAuthentication.RequestPasswordResetPath);
        cut.Find("#forgot-email").GetAttribute("type").Should().Be("email");
    }

    [Fact]
    public void TheAcknowledgementDoesNotSayWhetherAnythingWasSent()
    {
        var harness = new PasswordResetHarness();
        using var context = new Bunit.TestContext();

        var cut = RenderForgotPassword(context, harness, sent: "1");

        cut.Find("h1").TextContent.Should().Be("Check your email");
        // Conditional wording, and the condition is never evaluated on this page: an unknown
        // address, another store's address and an unconfirmed one all land here.
        cut.Markup.Should().Contain("If that address can sign in here");
        cut.FindAll("form").Should().BeEmpty("the form is replaced, so a second submit cannot probe");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    public void TheConfirmationRouteIsOfferedOnBothBranches(string? sent)
    {
        var harness = new PasswordResetHarness();
        using var context = new Bunit.TestContext();

        var cut = RenderForgotPassword(context, harness, sent);

        // An unconfirmed address gets no reset mail, so it needs somewhere to go — and that
        // somewhere has to be visible to everybody. Offering it only to the people it applies
        // to would tell them their address is known here.
        cut.FindAll($"a[href='{CustomerAuthentication.ConfirmEmailPath}']")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void ArrivingWithNoLinkOffersAFreshOneRatherThanAForm()
    {
        var harness = new PasswordResetHarness();
        using var context = new Bunit.TestContext();

        var cut = RenderResetPassword(context, harness);

        cut.Find("h1").TextContent.Should().Be("That link did not work");
        cut.FindAll("input[name='password']").Should().BeEmpty();
        cut.Find($"a[href='{CustomerAuthentication.ForgotPasswordPath}']");
    }

    [Fact]
    public void AFollowedLinkRendersTheFormCarryingBothHalvesOfIt()
    {
        var harness = new PasswordResetHarness();
        using var context = new Bunit.TestContext();

        var cut = RenderResetPassword(context, harness, userId: "user-1", token: EncodedToken);

        // In the body rather than only the query string, so the post does not depend on the
        // query surviving it. Both are revalidated by the service either way.
        cut.Find($"input[name='{MailedTokenLink.UserIdParameter}']")
            .GetAttribute("value").Should().Be("user-1");
        cut.Find($"input[name='{MailedTokenLink.TokenParameter}']")
            .GetAttribute("value").Should().Be(EncodedToken);
        cut.Find("input[name='password']").GetAttribute("type").Should().Be("password");
        cut.Find("input[name='confirm']").GetAttribute("type").Should().Be("password");
    }

    [Fact]
    public void TwoDifferentPasswordsAreRefusedWithoutSpendingTheLink()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();
        using var context = new Bunit.TestContext();

        var cut = RenderResetPassword(context, harness, user.Id, EncodedToken, FormPost(new()
        {
            [MailedTokenLink.UserIdParameter] = user.Id,
            [MailedTokenLink.TokenParameter] = EncodedToken,
            ["password"] = "N3w-Passw0rd!",
            ["confirm"] = "N3w-Passw0rd?"
        }));

        cut.Find("form").Submit();

        cut.Markup.Should().Contain("Those two passwords are not the same");
        // The token is untouched, so the customer still has a working link to retry with.
        harness.Users.DidNotReceive()
            .ResetPasswordAsync(Arg.Any<IdentityUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void ARejectedPasswordIsNeverEchoedBackIntoTheForm()
    {
        const string attempted = "nodigits!";

        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.ResetPasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequiresDigit",
                Description = "Passwords must have at least one digit ('0'-'9')."
            }));

        using var context = new Bunit.TestContext();

        var cut = RenderResetPassword(context, harness, user.Id, EncodedToken, FormPost(new()
        {
            [MailedTokenLink.UserIdParameter] = user.Id,
            [MailedTokenLink.TokenParameter] = EncodedToken,
            ["password"] = attempted,
            ["confirm"] = attempted
        }));

        cut.Find("form").Submit();

        // The rules are quoted so the customer can act on them; the password is not, on this
        // render or any other. A value attribute here would put it in the page, the browser's
        // cache and any proxy log that keeps bodies.
        cut.Markup.Should().Contain("at least one digit");
        cut.Markup.Should().NotContain(attempted);
        cut.Find("input[name='password']").HasAttribute("value").Should().BeFalse();
    }

    [Fact]
    public void AnUnusableLinkDoesNotOfferTheFormAgain()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.ResetPasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidToken",
                Description = "Invalid token."
            }));

        using var context = new Bunit.TestContext();

        var cut = RenderResetPassword(context, harness, user.Id, EncodedToken, FormPost(new()
        {
            [MailedTokenLink.UserIdParameter] = user.Id,
            [MailedTokenLink.TokenParameter] = EncodedToken,
            ["password"] = "N3w-Passw0rd!",
            ["confirm"] = "N3w-Passw0rd!"
        }));

        cut.Find("form").Submit();

        cut.Find("h1").TextContent.Should().Be("That link did not work");
        cut.FindAll("input[name='password']").Should().BeEmpty();
    }

    [Fact]
    public void ASuccessfulResetSendsTheCustomerToSignIn()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.ResetPasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);

        using var context = new Bunit.TestContext();

        var cut = RenderResetPassword(context, harness, user.Id, EncodedToken, FormPost(new()
        {
            [MailedTokenLink.UserIdParameter] = user.Id,
            [MailedTokenLink.TokenParameter] = EncodedToken,
            ["password"] = "N3w-Passw0rd!",
            ["confirm"] = "N3w-Passw0rd!"
        }));

        cut.Find("form").Submit();

        cut.Find("h1").TextContent.Should().Be("Password changed");
        cut.Find($"a[href='{CustomerAuthentication.LoginPath}']");
        // Stated on the page because it is now true, and because a customer resetting a
        // password they think somebody else has needs to know it took effect everywhere.
        cut.Markup.Should().Contain("has been signed out");
    }
}
