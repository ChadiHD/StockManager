using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Sites;
using StockManager.Notifications;

namespace SMStore.Tests.TestSupport;

/// <summary>
/// A real <see cref="PasswordResetService"/> over a substituted user store and mail sender.
/// </summary>
/// <remarks>
/// The service is built rather than substituted, here and in the page tests, for the same
/// reason <c>DocumentUploadService</c> is: what is worth testing is the order and the
/// conditions it encodes — which addresses get mail, which failures are told apart, which
/// store a token is good for — and replacing it wholesale would test none of that.
///
/// <see cref="UserManager{TUser}"/> is a class with nine constructor parameters and no
/// interface, so it is substituted with a store and eight nulls. Its own constructor tolerates
/// that (options fall back to <c>new IdentityOptions()</c>), and every method these tests
/// reach is virtual.
/// </remarks>
internal sealed class PasswordResetHarness
{
    public const string SiteKey = "test";

    public PasswordResetHarness(SiteModel? site = null)
    {
        Site = site ?? new SiteModel
        {
            Id = 1,
            SiteKey = SiteKey,
            Name = "Test store",
            Domain = "test.example",
            Country = "IE"
        };

        SiteContext = Substitute.For<ISiteContext>();
        SiteContext.Site.Returns(Site);
        SiteContext.IsResolved.Returns(true);

        Users = Substitute.For<UserManager<IdentityUser>>(
            Substitute.For<IUserStore<IdentityUser>>(),
            null, null, null, null, null, null, null, null);

        Sender = Substitute.For<IEmailSender>();

        Service = new PasswordResetService(
            Users, SiteContext, Sender, NullLogger<PasswordResetService>.Instance);
    }

    public SiteModel Site { get; }

    public ISiteContext SiteContext { get; }

    public UserManager<IdentityUser> Users { get; }

    public IEmailSender Sender { get; }

    public PasswordResetService Service { get; }

    /// <summary>The one message the service sent, or a failure if it sent none.</summary>
    public EmailMessage SentMessage() =>
        Sender.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEmailSender.SendAsync))
            .Select(call => (EmailMessage)call.GetArguments()[0]!)
            .Single();

    /// <summary>A confirmed customer login at this harness's store.</summary>
    public IdentityUser WithConfirmedUser(string email = "buyer@example.test", string? siteKey = null)
    {
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"{siteKey ?? Site.SiteKey}|{email}",
            Email = email,
            EmailConfirmed = true,
            SecurityStamp = "stamp-before"
        };

        Users.FindByNameAsync(user.UserName).Returns(user);
        Users.FindByIdAsync(user.Id).Returns(user);

        return user;
    }
}
