using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// The claim that stops two syncs importing one feed at once.
/// </summary>
/// <remarks>
/// Worth testing at this level because the ordering is the whole mechanism: the claim has to be
/// taken before anything is fetched, and a refused claim has to stop the run without looking
/// like a broken feed. Both are invisible from a passing sync, and both would survive a
/// refactor that moved the claim below the fetch.
/// </remarks>
public class DistributorFeedSyncServiceTests
{
    private readonly IDistributorFeedClient _client = Substitute.For<IDistributorFeedClient>();
    private readonly IProductData _products = Substitute.For<IProductData>();
    private readonly IDistributorFeedData _feeds = Substitute.For<IDistributorFeedData>();
    private readonly IFeedSecretStoreResolver _secrets = Substitute.For<IFeedSecretStoreResolver>();
    private readonly IFeedSecretStore _store = Substitute.For<IFeedSecretStore>();
    private readonly IImageEnrichmentSignal _enrichment = Substitute.For<IImageEnrichmentSignal>();
    private readonly DistributorFeedSyncService _sync;

    public DistributorFeedSyncServiceTests()
    {
        _secrets.For(Arg.Any<string>()).Returns(_store);
        _store.ResolveAsync(Arg.Any<string>()).Returns("secret");
        _client.Fetch(Arg.Any<DistributorFeedSettings>()).Returns(new List<DistributorFeedRecord>());
        _products.BulkUpsertFromFeed(Arg.Any<string>(), Arg.Any<IEnumerable<DistributorFeedRecord>>())
            .Returns(new FeedUpsertResult());

        _sync = new DistributorFeedSyncService(
            _client, _products, _feeds, _secrets, _enrichment,
            NullLogger<DistributorFeedSyncService>.Instance);
    }

    private static DistributorFeedModel Feed(int id = 3, int siteId = 7) => new()
    {
        Id = id,
        SiteId = siteId,
        Name = "Main",
        Host = "sftp.example.test",
        Username = "acli",
        SecretRef = "ciphertext",
        SecretProvider = "DataProtection",
        Enabled = true
    };

    private void Claimable(bool claimed) =>
        _feeds.ClaimForSync(Arg.Any<int>(), Arg.Any<int>()).Returns(claimed);

    [Fact]
    public async Task ARefusedClaimStopsTheRunBeforeAnythingIsFetched()
    {
        var feed = Feed();
        _feeds.GetFeedById(feed.Id, feed.SiteId).Returns(feed);
        Claimable(false);

        var result = await _sync.SyncAsync(feed.Id, feed.SiteId);

        result.AlreadyRunning.Should().BeTrue();
        result.Succeeded.Should().BeFalse("nothing was imported");

        // No SFTP session, and no credential resolved to open one with. A claim taken after the
        // fetch would still prevent the double import, and would still have opened two
        // connections to a distributor that may allow one.
        _client.DidNotReceive().Fetch(Arg.Any<DistributorFeedSettings>());
        await _store.DidNotReceive().ResolveAsync(Arg.Any<string>());
        _products.DidNotReceive().BulkUpsertFromFeed(
            Arg.Any<string>(), Arg.Any<IEnumerable<DistributorFeedRecord>>());
    }

    [Fact]
    public async Task ARefusedClaimDoesNotOverwriteTheStatusOfTheRunThatHoldsIt()
    {
        var feed = Feed();
        _feeds.GetFeedById(feed.Id, feed.SiteId).Returns(feed);
        Claimable(false);

        await _sync.SyncAsync(feed.Id, feed.SiteId);

        // RecordSync also releases the claim. Calling it here would hand the feed away from the
        // sync still running on it, which is the one way this guard could make things worse
        // than no guard at all.
        _feeds.DidNotReceive().RecordSync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public async Task AClaimedFeedIsSyncedAndTheClaimIsReleased()
    {
        var feed = Feed();
        _feeds.GetFeedById(feed.Id, feed.SiteId).Returns(feed);
        Claimable(true);

        var result = await _sync.SyncAsync(feed.Id, feed.SiteId);

        result.Succeeded.Should().BeTrue();
        result.AlreadyRunning.Should().BeFalse();

        _feeds.Received(1).ClaimForSync(feed.Id, feed.SiteId);
        _feeds.Received(1).RecordSync(feed.Id, Arg.Any<string>(), feed.SiteId);
    }

    [Fact]
    public async Task AFailedSyncStillReleasesTheClaim()
    {
        var feed = Feed();
        _feeds.GetFeedById(feed.Id, feed.SiteId).Returns(feed);
        Claimable(true);
        _client.Fetch(Arg.Any<DistributorFeedSettings>())
            .Returns(_ => throw new InvalidOperationException("connection refused"));

        var result = await _sync.SyncAsync(feed.Id, feed.SiteId);

        result.Succeeded.Should().BeFalse();

        // Otherwise a feed that fails once stops syncing until its lease expires, and the
        // symptom — a feed that will not start — looks nothing like the cause.
        _feeds.Received(1).RecordSync(feed.Id, Arg.Is<string>(status => status.StartsWith("Failed")), feed.SiteId);
    }

    [Fact]
    public async Task OneBusyFeedDoesNotStopTheOthersInASyncAll()
    {
        var busy = Feed(id: 1);
        var free = Feed(id: 2);
        _feeds.GetFeeds(7).Returns(new List<DistributorFeedModel> { busy, free });
        _feeds.ClaimForSync(busy.Id, 7).Returns(false);
        _feeds.ClaimForSync(free.Id, 7).Returns(true);

        var results = await _sync.SyncAllAsync(7);

        results.Should().HaveCount(2);
        results.Should().ContainSingle(result => result.AlreadyRunning);
        results.Should().ContainSingle(result => result.Succeeded);
    }

    [Fact]
    public async Task ADisabledFeedIsNotEvenClaimed()
    {
        var disabled = Feed(id: 4);
        disabled.Enabled = false;
        _feeds.GetFeeds(7).Returns(new List<DistributorFeedModel> { disabled });

        var results = await _sync.SyncAllAsync(7);

        results.Should().BeEmpty();
        _feeds.DidNotReceive().ClaimForSync(Arg.Any<int>(), Arg.Any<int>());
    }
}
