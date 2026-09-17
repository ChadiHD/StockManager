using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using SMStore.Accounts;
using SMStore.Tests.TestSupport;
using StockManager.Notifications;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// What password reset is allowed to reveal, and what a link is good for.
/// </summary>
/// <remarks>
/// Most of these are about the absence of a difference: an unknown address and a real one
/// have to leave the same trace, and an expired token and a user id that names nobody have to
/// produce the same answer. Those are the assertions that stop this becoming the
/// account-enumeration oracle the registration form and the sign-in page both refuse to be —
/// and the ones a later "helpful error message" would quietly delete.
/// </remarks>
public class PasswordResetServiceTests
{
    private const string EncodedToken = "cmVzZXQtdG9rZW4"; // base64url of "reset-token"

    [Fact]
    public async Task RequestMailsALinkBuiltFromTheSitesOwnDomain()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.GeneratePasswordResetTokenAsync(user).Returns("reset-token");

        await harness.Service.RequestAsync(user.Email);

        var message = harness.SentMessage();

        message.To.Should().Be(user.Email);
        message.SiteKey.Should().Be(harness.Site.SiteKey);
        // The host is the site's, never the request's: this link lands in an inbox, where one
        // pointing at an attacker's host and carrying a valid token is the whole prize.
        message.Body.Should().Contain($"https://{harness.Site.Domain}/reset-password?userId=");
        message.Body.Should().Contain($"token={EncodedToken}");
    }

    [Fact]
    public async Task RequestSendsNothingForAnAddressThatHasNoLoginHere()
    {
        var harness = new PasswordResetHarness();

        await harness.Service.RequestAsync("stranger@example.test");

        await harness.Sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestLooksTheAddressUpUnderThisStoresOwnLoginName()
    {
        var harness = new PasswordResetHarness();

        // The login this store would have: anything registered at another store is named
        // differently and therefore does not exist here at all, which is why no cross-store
        // comparison appears in RequestAsync.
        await harness.Service.RequestAsync("  buyer@example.test  ");

        await harness.Users.Received().FindByNameAsync("test|buyer@example.test");
    }

    [Fact]
    public async Task RequestSendsNothingToAnAddressNobodyHasConfirmed()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();
        user.EmailConfirmed = false;

        await harness.Service.RequestAsync(user.Email);

        // A reset token sent to an unconfirmed address is a credential handed to whoever typed
        // that address into the registration form. The route for that person is /confirm-email.
        await harness.Users.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<IdentityUser>());
        await harness.Sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestSwallowsASendFailureRatherThanReportingIt()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.GeneratePasswordResetTokenAsync(user).Returns("reset-token");
        harness.Sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("no transport")));

        // Not merely "does not crash": a thrown or reported failure would distinguish a real
        // address from one that produced no mail, which is the entire disclosure this method is
        // written to avoid.
        await harness.Service.RequestAsync(user.Email);
    }

    [Fact]
    public async Task ResetChangesThePasswordWhenTheLinkIsGood()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.ResetPasswordAsync(user, "reset-token", "N3w-Passw0rd!")
            .Returns(IdentityResult.Success);

        var outcome = await harness.Service.ResetAsync(user.Id, EncodedToken, "N3w-Passw0rd!");

        outcome.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ResetTellsTheCustomerTheirPasswordChanged()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.ResetPasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);

        await harness.Service.ResetAsync(user.Id, EncodedToken, "N3w-Passw0rd!");

        var message = harness.SentMessage();

        // The only signal the real owner gets when somebody else resets their password: the
        // reset mail itself went to a mailbox the attacker controls and was deleted.
        message.To.Should().Be(user.Email);
        message.Subject.Should().Contain("password was changed");
    }

    [Fact]
    public async Task ResetStillReportsSuccessWhenTheNotificationCannotBeSent()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.ResetPasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        harness.Sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("no transport")));

        var outcome = await harness.Service.ResetAsync(user.Id, EncodedToken, "N3w-Passw0rd!");

        // The password has already been written by the time the mail goes out. Reporting a
        // failure would send the customer back to a link that no longer works.
        outcome.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ResetRefusesATokenIssuedForAnotherStore()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser(siteKey: "other");

        var outcome = await harness.Service.ResetAsync(user.Id, EncodedToken, "N3w-Passw0rd!");

        // A token is evidence about one login and says nothing about which store issued it.
        // Without this check, store B would serve the reset for a customer of store A.
        outcome.Result.Should().Be(PasswordResetResult.LinkNotUsable);
        await harness.Users.DidNotReceive()
            .ResetPasswordAsync(Arg.Any<IdentityUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(null, EncodedToken)]
    [InlineData("nobody", EncodedToken)]
    [InlineData("nobody", "not-base64url!!")]
    [InlineData("nobody", null)]
    public async Task ResetAnswersEveryUnusableLinkIdentically(string? userId, string? token)
    {
        var harness = new PasswordResetHarness();

        var outcome = await harness.Service.ResetAsync(userId, token, "N3w-Passw0rd!");

        // No token, a mangled one, and a user id that names nobody: one answer, because
        // separating them would say which ids exist and the next step is the same regardless.
        outcome.Result.Should().Be(PasswordResetResult.LinkNotUsable);
        outcome.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetReportsAnExpiredTokenAsAnUnusableLinkAndNotAsABadPassword()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.ResetPasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidToken",
                Description = "Invalid token."
            }));

        var outcome = await harness.Service.ResetAsync(user.Id, EncodedToken, "N3w-Passw0rd!");

        outcome.Result.Should().Be(PasswordResetResult.LinkNotUsable);
        outcome.Messages.Should().BeEmpty("Identity's token wording says nothing a customer can act on");
    }

    [Fact]
    public async Task ResetQuotesThePasswordRulesWhenThoseAreWhatFailed()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        harness.Users.ResetPasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequiresDigit",
                Description = "Passwords must have at least one digit ('0'-'9')."
            }));

        var outcome = await harness.Service.ResetAsync(user.Id, EncodedToken, "nodigits!");

        // Safe to show, unlike a token error: these describe the store's rules rather than
        // anything about the account, and a form that refuses without saying why is a form
        // people give up on.
        outcome.Result.Should().Be(PasswordResetResult.PasswordRejected);
        outcome.Messages.Should().ContainSingle()
            .Which.Should().Contain("at least one digit");
    }

    [Fact]
    public async Task ResetDoesNotSpendTheTokenOnAnEmptyPassword()
    {
        var harness = new PasswordResetHarness();
        var user = harness.WithConfirmedUser();

        var outcome = await harness.Service.ResetAsync(user.Id, EncodedToken, "");

        outcome.Result.Should().Be(PasswordResetResult.LinkNotUsable);
        await harness.Users.DidNotReceive()
            .ResetPasswordAsync(Arg.Any<IdentityUser>(), Arg.Any<string>(), Arg.Any<string>());
    }
}
