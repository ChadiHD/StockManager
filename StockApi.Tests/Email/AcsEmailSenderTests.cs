using Azure;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StockApi.Email;
using StockManager.Notifications;
using Xunit;
using AcsEmail = Azure.Communication.Email;

namespace StockApi.Tests.Email;

// Which failures the dispatcher may retry and which it must give up on at once. The provider
// itself is out of reach of a unit test; what can go wrong here is the sorting.
public class AcsEmailSenderTests
{
    private readonly AcsEmail.EmailClient _client = Substitute.For<AcsEmail.EmailClient>();

    private static EmailMessage Message(string? from = "orders@shop.test.example") =>
        new("test", "ada@example.com", "Ada Byron", "Subject", "Body", from);

    [Fact]
    public async Task AStoreWithNoSenderAddressSendsNothingAndIsNotRetried()
    {
        var send = () => new AcsEmailSender(_client).SendAsync(Message(from: null));

        await send.Should().ThrowAsync<PermanentEmailFailureException>().WithMessage("*MailFromAddress*");
        await _client.DidNotReceiveWithAnyArgs().SendAsync(default, default(AcsEmail.EmailMessage)!, default);
    }

    [Fact]
    public async Task AMessageTheProviderRefusesIsNotRetried()
    {
        _client.SendAsync(Arg.Any<WaitUntil>(), Arg.Any<AcsEmail.EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(400, "Sender domain not verified"));

        var send = () => new AcsEmailSender(_client).SendAsync(Message());

        await send.Should().ThrowAsync<PermanentEmailFailureException>();
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task ThrottlingAndOutagesAreLeftForTheRetry(int status)
    {
        _client.SendAsync(Arg.Any<WaitUntil>(), Arg.Any<AcsEmail.EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(status, "busy"));

        var send = () => new AcsEmailSender(_client).SendAsync(Message());

        (await send.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(status);
    }
}
