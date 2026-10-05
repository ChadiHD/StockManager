using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Email;
using SMDataManager.Library.Models;
using StockApi.Email;
using StockManager.Notifications;
using Xunit;

namespace StockApi.Tests.Email;

/// <summary>
/// What the dispatcher does with each message it claims: send it, retry it, or give up on it
/// and tell somebody.
/// </summary>
/// <remarks>
/// The claim itself is the database's and <c>EmailOutboxTests</c> covers it there. Everything
/// here is the decision made per row once it has been claimed, against substitutes — which is
/// enough, because none of it depends on SQL.
/// </remarks>
public class EmailDispatcherTests
{
    private static readonly Guid Claim = Guid.NewGuid();

    private readonly IEmailOutboxData _outbox = Substitute.For<IEmailOutboxData>();
    private readonly IEmailOutbox _queue = Substitute.For<IEmailOutbox>();
    private readonly ISiteData _sites = Substitute.For<ISiteData>();
    private readonly ISiteEmailTemplateData _wording = Substitute.For<ISiteEmailTemplateData>();
    private readonly IEmailSender _transport = Substitute.For<IEmailSender>();
    private readonly OutboxPayloadProtector _protector =
        new(new EphemeralDataProtectionProvider());

    private static SiteModel Site(string? operatorEmail = "ops@test.example") => new()
    {
        Id = 3,
        SiteKey = "test",
        Name = "Test Store",
        Domain = "shop.test.example",
        OperatorEmail = operatorEmail!,
        IsActive = true
    };

    private EmailDispatcher Dispatcher(SiteModel? site = null)
    {
        _sites.GetSites().Returns([site ?? Site()]);
        _outbox.RecordSent(Arg.Any<int>(), Arg.Any<Guid>()).Returns(true);
        _outbox.RecordFailure(Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
            .Returns(true);

        return new EmailDispatcher(
            _outbox, _queue, _sites, _wording, _transport, _protector, NullLogger<EmailDispatcher>.Instance);
    }

    private void Claims(params EmailOutboxModel[] rows) =>
        _outbox.Claim(Arg.Any<Guid>(), EmailDispatcher.BatchSize, EmailDispatcher.LeaseMinutes)
            .Returns(rows.ToList());

    private static EmailOutboxModel Approval(int id = 11, int attempts = 1) => new()
    {
        Id = id,
        SiteId = 3,
        ToAddress = "ada@example.com",
        ToName = "Ada Byron",
        TemplateKey = EmailTemplates.AccountApproved.Key,
        PayloadJson = """{"company":"Acme Trading"}""",
        Status = EmailOutboxStatus.Sending,
        Attempts = attempts,
        ClaimToken = Claim,
    };

    [Fact]
    public async Task AClaimedMessageIsRenderedSentAndRecorded()
    {
        var dispatcher = Dispatcher();
        Claims(Approval());

        var claimed = await dispatcher.DispatchBatchAsync();

        claimed.Should().Be(1);
        await _transport.Received(1).SendAsync(
            Arg.Is<EmailMessage>(message =>
                message.SiteKey == "test"
                && message.To == "ada@example.com"
                && message.ToName == "Ada Byron"
                && message.Subject == "Your trade account is open — Test Store"
                && message.Body.Contains("Acme Trading")
                && message.Body.Contains("https://shop.test.example/login")),
            Arg.Any<CancellationToken>());
        _outbox.Received(1).RecordSent(11, Claim);
    }

    [Fact]
    public async Task AProtectedPayloadIsReadBackBeforeItIsRendered()
    {
        // Written by the storefront, read here: the shared key ring is what makes that work,
        // and one ring under one purpose string is what this stands in for.
        var json = EmailTemplates.RegistrationReceived.Serialize(
            new RegistrationReceivedPayload("AC-0042", "https://shop.test.example/confirm-email?t=secret"));

        var row = Approval();
        row.TemplateKey = EmailTemplates.RegistrationReceived.Key;
        row.PayloadJson = _protector.Protect(json);
        row.PayloadProtected = true;

        var dispatcher = Dispatcher();
        Claims(row);

        await dispatcher.DispatchBatchAsync();

        row.PayloadJson.Should().NotContain("secret", "the row holds ciphertext, not the link");
        await _transport.Received(1).SendAsync(
            Arg.Is<EmailMessage>(message => message.Body.Contains("confirm-email?t=secret")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedSendIsRetriedOnTheSchedule()
    {
        var dispatcher = Dispatcher();
        Claims(Approval(attempts: 3));
        _transport.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("421 try again later"));

        await dispatcher.DispatchBatchAsync();

        _outbox.Received(1).RecordFailure(11, Claim, "421 try again later", TimeSpan.FromMinutes(4));
        _outbox.DidNotReceive().RecordSent(Arg.Any<int>(), Arg.Any<Guid>());
        _queue.DidNotReceiveWithAnyArgs().Enqueue<MailUndeliverablePayload>(default, default!, default!, default!, default!);
    }

    [Fact]
    public async Task TheLastFailureDeadLettersTheMessageAndTellsTheOperator()
    {
        var dispatcher = Dispatcher();
        Claims(Approval(attempts: OutboxRetryPolicy.MaxAttempts));
        _transport.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("550 mailbox unavailable"));

        await dispatcher.DispatchBatchAsync();

        _outbox.Received(1).RecordFailure(11, Claim, "550 mailbox unavailable", null);
        _queue.Received(1).Enqueue(
            3, "ops@test.example", null, EmailTemplates.MailUndeliverable,
            Arg.Is<MailUndeliverablePayload>(payload =>
                payload.Messages.Count == 1
                && payload.Messages[0].OutboxId == 11
                && payload.Messages[0].Recipient == "ada@example.com"
                && payload.Messages[0].LastError == "550 mailbox unavailable"));
    }

    [Fact]
    public async Task SeveralDeadLettersInOneBatchAreOneAlert()
    {
        // A relay refusing everything for two hours would otherwise be one message per
        // customer, to an operator who needs to hear it once.
        var dispatcher = Dispatcher();
        Claims(Approval(11, OutboxRetryPolicy.MaxAttempts), Approval(12, OutboxRetryPolicy.MaxAttempts));
        _transport.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("550"));

        await dispatcher.DispatchBatchAsync();

        _queue.Received(1).Enqueue(
            3, "ops@test.example", null, EmailTemplates.MailUndeliverable,
            Arg.Is<MailUndeliverablePayload>(payload => payload.Messages.Count == 2));
    }

    [Fact]
    public async Task AnUndeliverableOperatorMessageGoesNoFurther()
    {
        // The alert about undeliverable mail is itself mail. If it cannot be delivered, telling
        // the same address about that is one turn of the wheel too many; the log is the record.
        var row = Approval(attempts: OutboxRetryPolicy.MaxAttempts);
        row.TemplateKey = EmailTemplates.MailUndeliverable.Key;
        row.PayloadJson = "{}";

        var dispatcher = Dispatcher();
        Claims(row);
        _transport.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("550"));

        await dispatcher.DispatchBatchAsync();

        _outbox.Received(1).RecordFailure(11, Claim, "550", null);
        _queue.DidNotReceiveWithAnyArgs().Enqueue<MailUndeliverablePayload>(default, default!, default!, default!, default!);
    }

    [Fact]
    public async Task AStoreWithNoOperatorAddressIsNotToldOnAnotherStoresBehalf()
    {
        // No platform-wide fallback: the alert names this store's customers.
        var dispatcher = Dispatcher(Site(operatorEmail: null));
        Claims(Approval(attempts: OutboxRetryPolicy.MaxAttempts));
        _transport.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("550"));

        await dispatcher.DispatchBatchAsync();

        _queue.DidNotReceiveWithAnyArgs().Enqueue<MailUndeliverablePayload>(default, default!, default!, default!, default!);
    }

    [Fact]
    public async Task APayloadTheKeyRingCannotReadIsGivenUpOnAtOnce()
    {
        // Two hours of backoff will not bring a key back into the ring.
        var row = Approval();
        row.TemplateKey = EmailTemplates.RegistrationReceived.Key;
        row.PayloadJson = new OutboxPayloadProtector(new EphemeralDataProtectionProvider()).Protect("{}");
        row.PayloadProtected = true;

        var dispatcher = Dispatcher();
        Claims(row);

        await dispatcher.DispatchBatchAsync();

        await _transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        _outbox.Received(1).RecordFailure(
            11, Claim, Arg.Is<string>(error => error.Contains("key ring")), null);
    }

    [Fact]
    public async Task AMessageThatKeepsTakingTheHostDownIsNotSentAgain()
    {
        // Claimed once more than it is allowed attempts means every earlier claim ended with
        // the host dying mid-send. Sending it again is how the host dies again.
        var dispatcher = Dispatcher();
        Claims(Approval(attempts: OutboxRetryPolicy.MaxAttempts + 1));

        await dispatcher.DispatchBatchAsync();

        await _transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        _outbox.Received(1).RecordFailure(
            11, Claim, Arg.Is<string>(error => error.Contains("Abandoned")), null);
    }

    [Fact]
    public async Task AnUnknownTemplateIsRetriedRatherThanDropped()
    {
        // A host older than the row may be the one dispatching. A newer one may yet arrive.
        var row = Approval();
        row.TemplateKey = "quote.something-from-the-future";

        var dispatcher = Dispatcher();
        Claims(row);

        await dispatcher.DispatchBatchAsync();

        _outbox.Received(1).RecordFailure(
            11, Claim, Arg.Is<string>(error => error.Contains("quote.something-from-the-future")),
            TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task OneBadRowDoesNotStopTheRestOfTheBatch()
    {
        var broken = Approval(11);
        broken.TemplateKey = "no.such.template";

        var dispatcher = Dispatcher();
        Claims(broken, Approval(12));

        await dispatcher.DispatchBatchAsync();

        _outbox.Received(1).RecordSent(12, Claim);
    }

    [Fact]
    public async Task AStoresOwnWordingIsUsedForItsOwnCustomers()
    {
        _wording.Get(3, EmailTemplates.AccountApproved.Key).Returns(new SiteEmailTemplateModel
        {
            SiteId = 3,
            TemplateKey = EmailTemplates.AccountApproved.Key,
            Subject = "Welcome aboard, {Company}",
            Body = "Sign in at {SignInLink}.\n\n— The {SiteName} team",
        });

        var dispatcher = Dispatcher();
        Claims(Approval());

        await dispatcher.DispatchBatchAsync();

        await _transport.Received(1).SendAsync(
            Arg.Is<EmailMessage>(message =>
                message.Subject == "Welcome aboard, Acme Trading"
                && message.Body.StartsWith("Sign in at https://shop.test.example/login.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WordingThatCannotWorkFallsBackToThePlatformsAndTheMessageStillGoes()
    {
        // A store's typo is not a reason to leave its customer untold. The platform's words go,
        // and the warning is how somebody fixes the row.
        _wording.Get(3, EmailTemplates.AccountApproved.Key).Returns(new SiteEmailTemplateModel
        {
            SiteId = 3,
            TemplateKey = EmailTemplates.AccountApproved.Key,
            Subject = "Welcome, {Compnay}",
        });

        var dispatcher = Dispatcher();
        Claims(Approval());

        await dispatcher.DispatchBatchAsync();

        await _transport.Received(1).SendAsync(
            Arg.Is<EmailMessage>(message => message.Subject == "Your trade account is open — Test Store"),
            Arg.Any<CancellationToken>());
        _outbox.Received(1).RecordSent(11, Claim);
    }

    [Fact]
    public async Task OneStoresWordingIsReadOncePerBatch()
    {
        var dispatcher = Dispatcher();
        Claims(Approval(11), Approval(12), Approval(13));

        await dispatcher.DispatchBatchAsync();

        _wording.Received(1).Get(3, EmailTemplates.AccountApproved.Key);
    }

    [Fact]
    public async Task NothingDueIsNothingDone()
    {
        var dispatcher = Dispatcher();
        Claims();

        var claimed = await dispatcher.DispatchBatchAsync();

        claimed.Should().Be(0);
        _sites.DidNotReceive().GetSites();
    }
}
