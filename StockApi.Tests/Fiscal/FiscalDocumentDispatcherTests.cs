using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Email;
using SMDataManager.Library.Models;
using StockApi.Fiscal;
using Xunit;

namespace StockApi.Tests.Fiscal;

/// <summary>
/// What the fiscal dispatcher does with each document it claims (T14): issue it once, retry it, or
/// give up on it and tell the store's operator.
/// </summary>
/// <remarks>
/// The claim and the enqueue are the database's, and <c>FiscalDocumentTests</c> covers them there.
/// </remarks>
public class FiscalDocumentDispatcherTests
{
    private static readonly Guid Claim = Guid.NewGuid();

    private readonly IFiscalDocumentData _documents = Substitute.For<IFiscalDocumentData>();
    private readonly IOrderData _orders = Substitute.For<IOrderData>();
    private readonly ISiteData _sites = Substitute.For<ISiteData>();
    private readonly IFiscalDocumentIssuer _issuer = Substitute.For<IFiscalDocumentIssuer>();
    private readonly IEmailOutbox _mail = Substitute.For<IEmailOutbox>();

    private FiscalDocumentDispatcher Dispatcher(string? operatorEmail = "ops@test.example")
    {
        _sites.GetSites().Returns([new SiteModel { Id = 3, SiteKey = "br", OperatorEmail = operatorEmail! }]);
        _orders.GetOrderByReference("SO-0042", 3).Returns(new OrderModel { Id = 42, Reference = "SO-0042", FinalPrice = 90m });
        _orders.GetOrderLines(42, 3).Returns([new OrderLineModel { Name = "Shirt", Quantity = 1, Price = 90m }]);
        _documents.RecordIssued(default, default, default!, default!).ReturnsForAnyArgs(true);
        _documents.RecordFailure(default, default, default!, default).ReturnsForAnyArgs(true);
        _issuer.Key.Returns("acme-nfe");

        return new FiscalDocumentDispatcher(
            _documents, _orders, _sites, [_issuer], _mail, NullLogger<FiscalDocumentDispatcher>.Instance);
    }

    private void Claims(string issuer = "acme-nfe", int attempts = 1) =>
        _documents.Claim(Arg.Any<Guid>(), FiscalDocumentDispatcher.BatchSize, FiscalDocumentDispatcher.LeaseMinutes)
            .Returns([new FiscalDocumentModel
            {
                Id = 7, SiteId = 3, PurchaseId = 42, Reference = "SO-0042", Issuer = issuer,
                Attempts = attempts, ClaimToken = Claim
            }]);

    [Fact]
    public async Task AClaimedDocumentIsIssuedFromTheOrderAndItsNumberRecorded()
    {
        var dispatcher = Dispatcher();
        Claims();
        _issuer.IssueAsync(default!, default).ReturnsForAnyArgs(new FiscalDocumentIssued("35261012345", "https://nfe.test/35261012345"));

        (await dispatcher.DispatchBatchAsync()).Should().Be(1);

        await _issuer.Received(1).IssueAsync(
            Arg.Is<FiscalDocumentRequest>(request =>
                request.Site.SiteKey == "br" && request.Order.Reference == "SO-0042" && request.Lines.Count == 1),
            Arg.Any<CancellationToken>());
        _documents.Received(1).RecordIssued(7, Claim, "35261012345", "https://nfe.test/35261012345");
        _documents.DidNotReceiveWithAnyArgs().RecordFailure(default, default, default!, default);
    }

    [Fact]
    public async Task AFailureIsRetriedLater()
    {
        var dispatcher = Dispatcher();
        Claims();
        _issuer.IssueAsync(default!, default).ThrowsAsyncForAnyArgs(new HttpRequestException("The provider is down."));

        await dispatcher.DispatchBatchAsync();

        _documents.Received(1).RecordFailure(7, Claim, "The provider is down.", OutboxRetryPolicy.After(1));
        _mail.DidNotReceiveWithAnyArgs().Enqueue<FiscalFailedPayload>(default, default!, default, default!, default!);
    }

    [Fact]
    public async Task ARefusalIsGivenUpOnAtOnceAndTheOperatorIsTold()
    {
        var dispatcher = Dispatcher();
        Claims();
        _issuer.IssueAsync(default!, default).ThrowsAsyncForAnyArgs(new FiscalDocumentRefusedException("Rejeição 539: duplicate."));

        await dispatcher.DispatchBatchAsync();

        _documents.Received(1).RecordFailure(7, Claim, "Rejeição 539: duplicate.", null);
        _mail.Received(1).Enqueue(3, "ops@test.example", null, EmailTemplates.FiscalFailed,
            Arg.Is<FiscalFailedPayload>(payload => payload.Reference == "SO-0042" && payload.Issuer == "acme-nfe"));
    }

    [Fact]
    public async Task TheLastRetryGivesUpAndAStoreWithNoOperatorIsNotMailed()
    {
        var dispatcher = Dispatcher(operatorEmail: null);
        Claims(attempts: OutboxRetryPolicy.MaxAttempts);
        _issuer.IssueAsync(default!, default).ThrowsAsyncForAnyArgs(new TimeoutException("Timed out."));

        await dispatcher.DispatchBatchAsync();

        _documents.Received(1).RecordFailure(7, Claim, "Timed out.", null);
        _mail.DidNotReceiveWithAnyArgs().Enqueue<FiscalFailedPayload>(default, default!, default, default!, default!);
    }

    [Fact]
    public async Task AnIssuerThisBuildDoesNotKnowIsRetriedRatherThanDropped()
    {
        var dispatcher = Dispatcher();
        Claims(issuer: "from-a-newer-host");

        await dispatcher.DispatchBatchAsync();

        await _issuer.DidNotReceiveWithAnyArgs().IssueAsync(default!, default);
        _documents.Received(1).RecordFailure(7, Claim, Arg.Is<string>(error => error.Contains("from-a-newer-host")),
            OutboxRetryPolicy.After(1));
    }

    [Fact]
    public async Task TheLoggingIssuerIssuesAPlainlyUnrealNumber()
    {
        var issued = await new LoggingFiscalIssuer(NullLogger<LoggingFiscalIssuer>.Instance).IssueAsync(
            new FiscalDocumentRequest(new SiteModel { SiteKey = "br" }, new OrderModel { Reference = "SO-0042" }, [], null),
            CancellationToken.None);

        issued.Number.Should().Be("LOG-SO-0042");
    }

    [Fact]
    public void EveryIssuerKeyHasAnImplementationAndTheReverse()
    {
        // The settings screen offers SiteSettingKeys.FiscalIssuers and the API accepts nothing
        // else. A key with no issuer queues documents nobody can issue; an issuer with no key is one
        // no store can choose. 'none' is the one key with nothing behind it, by design.
        var services = new ServiceCollection().AddLogging().AddFiscalIssuers();
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IFiscalDocumentIssuer>().Select(issuer => issuer.Key).Append(SiteSettingKeys.NoFiscalIssuer)
            .Should().BeEquivalentTo(SiteSettingKeys.FiscalIssuers);
    }
}
