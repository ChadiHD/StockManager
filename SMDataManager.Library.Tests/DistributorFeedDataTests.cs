using FluentAssertions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// That a siteId reaches every feed procedure — see the remarks on IDistributorFeedData for why
/// this table is the one where a missing predicate does the most damage.
/// </summary>
/// <remarks>
/// Only the calls T4 added are covered here. The older ones went in before this project
/// existed and are worth adding when something touches them; what is new is worth holding now,
/// while the shape is still being decided.
/// </remarks>
public class DistributorFeedDataTests
{
    private readonly ISqlDataAccess _sql = Substitute.For<ISqlDataAccess>();
    private readonly DistributorFeedData _data;

    public DistributorFeedDataTests()
    {
        _data = new DistributorFeedData(_sql);

        _sql.LoadData<int, object>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new List<int>());
        _sql.LoadData<FeedSyncLogModel, object>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new List<FeedSyncLogModel>());
    }

    private static bool Site(object parameters, int siteId) =>
        SqlParameterMatch.Has(parameters, "SiteId", siteId);

    [Fact]
    public void ClaimForSyncIsRefusedWhenTheProcedureReportsNothingClaimed()
    {
        _sql.LoadData<int, object>("dbo.spDistributorFeed_ClaimForSync", Arg.Any<object>(), "SMDatabase")
            .Returns(new List<int> { 0 });

        _data.ClaimForSync(3, 7).Should().BeFalse();
    }

    [Fact]
    public void ClaimForSyncIsTakenOnlyOnAnExplicitOne()
    {
        _sql.LoadData<int, object>("dbo.spDistributorFeed_ClaimForSync", Arg.Any<object>(), "SMDatabase")
            .Returns(new List<int> { 1 });

        _data.ClaimForSync(3, 7).Should().BeTrue();
    }

    [Fact]
    public void ClaimForSyncTreatsAnEmptyResultAsRefused()
    {
        // The procedure always selects a row, so this is defensive — but the safe default
        // matters: read the other way, a database that answered oddly would hand out the claim
        // to everyone who asked.
        _data.ClaimForSync(3, 7).Should().BeFalse();
    }

    [Fact]
    public void ClaimForSyncPassesTheSiteId()
    {
        _data.ClaimForSync(3, 7);

        _sql.Received(1).LoadData<int, object>(
            "dbo.spDistributorFeed_ClaimForSync", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void RecordSyncPassesTheSiteIdAndTheTriggerAsItsName()
    {
        _data.RecordSync(3, 7, new FeedSyncRecord
        {
            StartedUtc = new DateTime(2026, 9, 17, 2, 0, 0, DateTimeKind.Utc),
            Succeeded = true,
            Status = "Imported 10 of 10; 0 delisted.",
            RecordCount = 10,
            Imported = 10,
            TriggeredBy = FeedSyncTrigger.Schedule
        });

        _sql.Received(1).SaveData("dbo.spDistributorFeed_RecordSync", Arg.Is<object>(p =>
            Site(p, 7) && SqlParameterMatch.Has(p, "TriggeredBy", "Schedule")), "SMDatabase");
    }

    [Fact]
    public void SyncHistoryPassesTheSiteIdAlongsideTheFeedId()
    {
        _data.GetSyncHistory(3, 7, 50);

        // The feed id alone identifies the row, so the site predicate here is not about finding
        // the right history — it is about not returning another store's, which quotes that
        // store's distributor hostnames back out of exception text.
        _sql.Received(1).LoadData<FeedSyncLogModel, object>(
            "dbo.spDistributorFeedSync_GetByFeed",
            Arg.Is<object>(p => Site(p, 7) && SqlParameterMatch.Has(p, "FeedId", 3)), "SMDatabase");
    }

    [Fact]
    public void RecentSyncsPassesTheSiteId()
    {
        _data.GetRecentSyncs(7, 20);

        _sql.Received(1).LoadData<FeedSyncLogModel, object>(
            "dbo.spDistributorFeedSync_GetRecent", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }
}
