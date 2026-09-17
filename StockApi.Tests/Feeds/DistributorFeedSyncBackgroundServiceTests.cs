using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;
using StockApi.Feeds;
using Xunit;

namespace StockApi.Tests.Feeds;

/// <summary>
/// The nightly scheduler: when it decides to run, and what it does to every store when it does.
/// </summary>
/// <remarks>
/// The scheduling arithmetic is tested directly rather than by waiting for it. What the tests
/// cannot reach is the loop itself, so <c>SyncEverySiteAsync</c> is internal and driven here:
/// the properties worth holding are that one store's failure does not stop another's, and that
/// an inactive store is not synced at all.
/// </remarks>
public class DistributorFeedSyncBackgroundServiceTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();

    private static SiteModel Site(int id, string key, bool active = true) =>
        new() { Id = id, SiteKey = key, Name = key, Domain = $"{key}.example", IsActive = active };

    [Theory]
    // Today's slot when it is still ahead.
    [InlineData("02:00", "2026-09-17T01:30:00Z", 0.5)]
    // Tomorrow's when it has passed, rather than immediately: a restart loop must not
    // re-import every feed on every restart.
    [InlineData("02:00", "2026-09-17T02:30:00Z", 23.5)]
    // And exactly on the hour counts as passed, so one tick of clock drift cannot run twice.
    [InlineData("02:00", "2026-09-17T02:00:00Z", 24)]
    [InlineData("23:45", "2026-09-17T23:44:00Z", 0.0166666)]
    public void TheNextRunIsTodaysSlotOrTomorrows(string syncTime, string nowUtc, double expectedHours)
    {
        var wait = DistributorFeedSyncBackgroundService.UntilNext(
            TimeSpan.Parse(syncTime), DateTime.Parse(nowUtc).ToUniversalTime());

        wait.TotalHours.Should().BeApproximately(expectedHours, 0.001);
    }

    [Fact]
    public void AnUnsetSyncTimeFallsBackRatherThanRefusing()
    {
        DistributorFeedSyncBackgroundService.TryReadSyncTime(
            Config(("Feeds:SyncEnabled", "true")), NullLogger.Instance, out var syncTime)
            .Should().BeTrue();

        syncTime.Should().Be(TimeSpan.FromHours(2));
    }

    [Theory]
    [InlineData("half past two")]
    [InlineData("25:00")]
    [InlineData("-01:00")]
    public void AnUnreadableSyncTimeStopsTheSchedulerRatherThanGuessing(string configured)
    {
        // Defaulting would sync at some hour nobody chose while the configuration file plainly
        // said otherwise, and nothing in the log would explain the difference.
        DistributorFeedSyncBackgroundService.TryReadSyncTime(
            Config(("Feeds:SyncAtUtc", configured)), NullLogger.Instance, out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task EveryActiveSiteIsSyncedAndInactiveOnesAreNot()
    {
        var sync = Substitute.For<IDistributorFeedSyncService>();
        sync.SyncAllAsync(Arg.Any<int>(), Arg.Any<FeedSyncTrigger>())
            .Returns(new List<DistributorFeedResult>());

        var service = Build(sync, Site(1, "acli"), Site(2, "dormant", active: false), Site(3, "second"));

        await service.SyncEverySiteAsync(CancellationToken.None);

        await sync.Received(1).SyncAllAsync(1, FeedSyncTrigger.Schedule);
        await sync.Received(1).SyncAllAsync(3, FeedSyncTrigger.Schedule);

        // An inactive store's storefront answers nothing, so importing its distributor's
        // catalog nightly spends a distributor's rate limit on rows no customer can see.
        await sync.DidNotReceive().SyncAllAsync(2, Arg.Any<FeedSyncTrigger>());
    }

    [Fact]
    public async Task TheRunIsRecordedAsScheduledRatherThanAsAnOperators()
    {
        var sync = Substitute.For<IDistributorFeedSyncService>();
        sync.SyncAllAsync(Arg.Any<int>(), Arg.Any<FeedSyncTrigger>())
            .Returns(new List<DistributorFeedResult>());

        await Build(sync, Site(1, "acli")).SyncEverySiteAsync(CancellationToken.None);

        // A feed that only ever succeeds when somebody presses the button is a scheduler
        // problem rather than a feed problem, and the trigger is what tells the two apart.
        await sync.DidNotReceive().SyncAllAsync(Arg.Any<int>(), FeedSyncTrigger.Operator);
    }

    [Fact]
    public async Task OneStoresFailureDoesNotStopAnothersSync()
    {
        var sync = Substitute.For<IDistributorFeedSyncService>();
        sync.SyncAllAsync(1, Arg.Any<FeedSyncTrigger>())
            .Returns<List<DistributorFeedResult>>(_ => throw new InvalidOperationException("SFTP down"));
        sync.SyncAllAsync(3, Arg.Any<FeedSyncTrigger>())
            .Returns(new List<DistributorFeedResult>());

        var service = Build(sync, Site(1, "acli"), Site(3, "second"));

        await service.SyncEverySiteAsync(CancellationToken.None);

        // Separate businesses on one deployment. The platform's whole promise is that they do
        // not share fate, and a store whose distributor is unreachable must not cost another
        // store its nightly import.
        await sync.Received(1).SyncAllAsync(3, FeedSyncTrigger.Schedule);
    }

    [Fact]
    public async Task ASiteListThatCannotBeReadEndsThePassRatherThanTheService()
    {
        var sites = Substitute.For<ISiteData>();
        sites.GetSites().Returns(_ => throw new InvalidOperationException("database asleep"));

        var sync = Substitute.For<IDistributorFeedSyncService>();
        var service = Build(sync, sites);

        // A database unreadable now may be readable tomorrow, so this must not throw out of the
        // loop — a BackgroundService that throws is gone for the life of the process.
        await service.SyncEverySiteAsync(CancellationToken.None);

        await sync.DidNotReceive().SyncAllAsync(Arg.Any<int>(), Arg.Any<FeedSyncTrigger>());
    }

    private static DistributorFeedSyncBackgroundService Build(
        IDistributorFeedSyncService sync, params SiteModel[] sites)
    {
        var siteData = Substitute.For<ISiteData>();
        siteData.GetSites().Returns(sites.ToList());

        return Build(sync, siteData);
    }

    private static DistributorFeedSyncBackgroundService Build(
        IDistributorFeedSyncService sync, ISiteData siteData)
    {
        var services = new ServiceCollection();
        services.AddSingleton(siteData);
        services.AddSingleton(sync);

        return new DistributorFeedSyncBackgroundService(
            services.BuildServiceProvider(),
            Config(("Feeds:SyncEnabled", "true")),
            NullLogger<DistributorFeedSyncBackgroundService>.Instance);
    }
}
