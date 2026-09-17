using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Feeds;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Feeds;

/// <summary>
/// How a sync outcome is reported, which is a different question from whether it worked.
/// </summary>
/// <remarks>
/// All of this exists to keep one distinction: a feed that was already syncing is not a feed
/// that failed. Once T4 runs feeds nightly, an operator pressing Sync inside that window is the
/// ordinary case, and answering it with a bad gateway would put a red banner in front of
/// somebody who did nothing wrong — which is how an operator learns to ignore the banner that
/// matters.
/// </remarks>
public class FeedSyncReportingTests
{
    private readonly IDistributorFeedData _feeds = Substitute.For<IDistributorFeedData>();
    private readonly IDistributorFeedSyncService _sync = Substitute.For<IDistributorFeedSyncService>();
    private readonly IFeedSecretStoreResolver _secrets = Substitute.For<IFeedSecretStoreResolver>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly DistributorFeedController _controller;

    public FeedSyncReportingTests()
    {
        _site.SiteId.Returns(42);
        _controller = new DistributorFeedController(_feeds, _sync, _secrets, _site);
    }

    private static DistributorFeedResult Busy() =>
        new() { Distributor = "Main", AlreadyRunning = true, Error = "A sync of this feed is already running." };

    private static DistributorFeedResult Failed() =>
        new() { Distributor = "Main", Succeeded = false, Error = "connection refused" };

    private static DistributorFeedResult Imported() =>
        new() { Distributor = "Main", Succeeded = true, RecordCount = 10, Imported = 10 };

    [Fact]
    public async Task SyncAnswersConflictWhenTheFeedIsAlreadySyncing()
    {
        _sync.SyncAsync(3, 42, FeedSyncTrigger.Operator).Returns(Busy());

        var result = await _controller.Sync(3);

        result.Result.Should().BeOfType<ConflictObjectResult>(
            "nothing is wrong with the feed or the request — something else holds the claim");
    }

    [Fact]
    public async Task SyncAnswersBadGatewayWhenTheFeedActuallyFailed()
    {
        _sync.SyncAsync(3, 42, FeedSyncTrigger.Operator).Returns(Failed());

        var result = await _controller.Sync(3);

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public void ABatchOfNothingButBusyFeedsIsNotAFailure()
    {
        FeedSyncOutcome.IsTotalFailure([Busy(), Busy()]).Should().BeFalse();
    }

    [Fact]
    public void ABatchIsAFailureOnlyWhenEveryFeedFailedForRealReasons()
    {
        FeedSyncOutcome.IsTotalFailure([Failed(), Failed()]).Should().BeTrue();

        // One busy feed is enough to stop the batch reading as broken: the run holding that
        // claim will report its own outcome, and this request cannot know what it will be.
        FeedSyncOutcome.IsTotalFailure([Failed(), Busy()]).Should().BeFalse();
        FeedSyncOutcome.IsTotalFailure([Failed(), Imported()]).Should().BeFalse();
    }

    [Fact]
    public void AStoreWithNoFeedsHasNotFailedToSyncThem()
    {
        // A store that has configured no feeds, or disabled all of them, returns an empty
        // batch. Reporting that as a bad gateway would make the nightly scheduler's own
        // logging shout about every tenant that does not use feeds.
        FeedSyncOutcome.IsTotalFailure([]).Should().BeFalse();
    }
}
