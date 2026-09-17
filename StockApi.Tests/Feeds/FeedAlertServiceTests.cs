using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Feeds;
using StockManager.Notifications;
using Xunit;

namespace StockApi.Tests.Feeds;

/// <summary>
/// Which feed problems are worth waking somebody for.
/// </summary>
/// <remarks>
/// Almost all of this is about not sending mail. A nightly sync that mails on every failing run
/// gets the eighth message filtered by the recipient, and that filter is still in place when
/// the next real failure happens — so the alert that matters is the one nobody sees. The rules
/// that prevent it are invisible from a working alert, which is what makes them worth pinning
/// down here.
/// </remarks>
public class FeedAlertServiceTests
{
    private readonly IDistributorFeedData _feeds = Substitute.For<IDistributorFeedData>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly FeedAlertService _alerts;

    public FeedAlertServiceTests()
    {
        _alerts = new FeedAlertService(_feeds, _sender, NullLogger<FeedAlertService>.Instance);

        _feeds.GetStaleFeeds(Arg.Any<int>()).Returns([]);
        _feeds.GetSyncHistory(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns([]);
    }

    private static SiteModel Site(string? operatorEmail = "ops@example.test", bool hideStale = false) => new()
    {
        Id = 7,
        SiteKey = "acli",
        Name = "Acli Trade",
        Domain = "acli.example",
        FeedStaleAfterHours = 26,
        HideStaleProducts = hideStale,
        OperatorEmail = operatorEmail
    };

    private void FeedExists(int id = 3, string name = "Main") =>
        _feeds.GetFeeds(7).Returns([new DistributorFeedModel { Id = id, SiteId = 7, Name = name }]);

    private void HistoryIs(int feedId, params bool[] succeededNewestFirst) =>
        _feeds.GetSyncHistory(feedId, 7, Arg.Any<int>()).Returns(
            succeededNewestFirst
                .Select(succeeded => new FeedSyncLogModel { FeedId = feedId, Succeeded = succeeded })
                .ToList());

    private static DistributorFeedResult Failed(string name = "Main") =>
        new() { Distributor = name, Succeeded = false, Error = "connection refused" };

    private static DistributorFeedResult Imported(string name = "Main") =>
        new() { Distributor = name, Succeeded = true, RecordCount = 10, Imported = 10 };

    private static DistributorFeedResult Busy(string name = "Main") =>
        new() { Distributor = name, AlreadyRunning = true };

    private async Task<List<EmailMessage>> SentBy(Func<Task> act)
    {
        await act();

        return _sender.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEmailSender.SendAsync))
            .Select(call => (EmailMessage)call.GetArguments()[0]!)
            .ToList();
    }

    [Fact]
    public async Task AFeedThatHasJustStartedFailingIsReported()
    {
        FeedExists();
        HistoryIs(3, succeededNewestFirst: [false, true]);

        var sent = await SentBy(() => _alerts.ReportAsync(Site(), [Failed()]));

        sent.Should().ContainSingle();
        sent[0].To.Should().Be("ops@example.test");
        sent[0].Subject.Should().Contain("Main");
    }

    [Fact]
    public async Task AFeedThatFailedLastNightToIsNotReportedAgain()
    {
        FeedExists();
        HistoryIs(3, succeededNewestFirst: [false, false]);

        var sent = await SentBy(() => _alerts.ReportAsync(Site(), [Failed()]));

        sent.Should().BeEmpty(
            "seven identical mails get the eighth filtered, and the filter is still there when "
            + "the next real failure happens");
    }

    [Fact]
    public async Task AFirstEverAttemptThatFailsIsReported()
    {
        FeedExists();
        HistoryIs(3, succeededNewestFirst: [false]);

        var sent = await SentBy(() => _alerts.ReportAsync(Site(), [Failed()]));

        // Nothing came before, so there is no repetition to suppress.
        sent.Should().ContainSingle();
    }

    [Fact]
    public async Task AHistoryThatCannotBeReadErrsTowardsTellingSomebody()
    {
        FeedExists();
        _feeds.GetSyncHistory(3, 7, Arg.Any<int>())
            .Returns(_ => throw new InvalidOperationException("database asleep"));

        var sent = await SentBy(() => _alerts.ReportAsync(Site(), [Failed()]));

        // One message too many beats silence, which is the failure mode this class exists to
        // prevent.
        sent.Should().ContainSingle();
    }

    [Fact]
    public async Task NothingIsSentForASuccessOrForABusyFeed()
    {
        FeedExists();

        var sent = await SentBy(() => _alerts.ReportAsync(Site(), [Imported(), Busy("Secondary")]));

        sent.Should().BeEmpty();
        // Not even looked up: a feed that was already syncing will report its own outcome, and
        // this pass knows nothing about what that will be.
        _feeds.DidNotReceive().GetSyncHistory(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task AStoreWithNoOperatorEmailIsNotMailedAndDoesNotThrow()
    {
        FeedExists();
        HistoryIs(3, succeededNewestFirst: [false, true]);

        var sent = await SentBy(() => _alerts.ReportAsync(Site(operatorEmail: null), [Failed()]));

        // There is deliberately no platform-wide fallback address: this message names the
        // store's distributor, and delivering it to another tenant's operator would be a
        // disclosure rather than a convenience. The finding is logged instead.
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task StaleFeedsAreReportedInOneMessage()
    {
        FeedExists();
        _feeds.GetStaleFeeds(7).Returns([
            new DistributorFeedModel { Id = 3, SiteId = 7, Name = "Main" },
            new DistributorFeedModel { Id = 4, SiteId = 7, Name = "Secondary" }
        ]);

        var sent = await SentBy(() => _alerts.ReportAsync(Site(), [Imported("Third")]));

        // Staleness is usually caused by something upstream of any one feed — the scheduler
        // off, the host down — so one message per feed would be several copies of one problem.
        sent.Should().ContainSingle();
        sent[0].Body.Should().Contain("Main").And.Contain("Secondary");
    }

    [Fact]
    public async Task AFeedThatFailedThisPassDoesNotAlsoGetAStalenessMessage()
    {
        FeedExists();
        HistoryIs(3, succeededNewestFirst: [false, true]);
        _feeds.GetStaleFeeds(7).Returns([new DistributorFeedModel { Id = 3, SiteId = 7, Name = "Main" }]);

        var sent = await SentBy(() => _alerts.ReportAsync(Site(), [Failed("Main")]));

        sent.Should().ContainSingle("the failure is the message; the staleness is the same event");
        sent[0].Subject.Should().Contain("failed");
    }

    [Fact]
    public async Task AFeedFailingRepeatedlyStaysSilentOnBothPaths()
    {
        FeedExists();
        HistoryIs(3, succeededNewestFirst: [false, false]);
        _feeds.GetStaleFeeds(7).Returns([new DistributorFeedModel { Id = 3, SiteId = 7, Name = "Main" }]);

        var sent = await SentBy(() => _alerts.ReportAsync(Site(), [Failed("Main")]));

        // The failure path is deliberately silent on a repeat. Letting the staleness path mail
        // it instead would undo exactly that, one night later.
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TheStalenessMessageSaysWhetherProductsAreStillOnSale()
    {
        FeedExists();
        _feeds.GetStaleFeeds(7).Returns([new DistributorFeedModel { Id = 3, SiteId = 7, Name = "Main" }]);

        var hiding = await SentBy(() => _alerts.ReportAsync(Site(hideStale: true), []));

        // "Your products have disappeared" and "you are selling on unconfirmed figures" call
        // for different urgency, and an operator should not have to read a column to find out
        // which applies.
        hiding[0].Body.Should().Contain("hidden from the storefront");
    }

    [Fact]
    public async Task ASendFailureNeverEscapes()
    {
        FeedExists();
        HistoryIs(3, succeededNewestFirst: [false, true]);
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("no transport")));

        // The import has already committed by the time this runs. An alert that failed to send
        // must not be reported as a sync that did not happen.
        await _alerts.ReportAsync(Site(), [Failed()]);
    }
}
