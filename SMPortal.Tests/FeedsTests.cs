using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMPortal.Models;
using SMPortal.Pages.Admin.Feeds;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// Feeds.razor: the pull half of "a failed sync is visible". The mail FeedAlertService sends is
// the push half, and neither is enough alone -- nobody watches a screen at 02:00, and a mail
// about a failure that has since been fixed is worse than no mail. What this page has to get
// right is saying nothing when nothing is wrong, saying the right thing when something is, and
// not saying either from data it made up.
public class FeedsTests : TestContext
{
    private static DistributorFeedView Feed(int id, string name, DateTime? syncStarted = null) => new()
    {
        Id = id,
        Name = name,
        Host = "sftp.example.test",
        Username = "acli",
        HasCredential = true,
        Enabled = true,
        SyncStartedUtc = syncStarted
    };

    private static FeedSyncLogView Attempt(
        int feedId, string feedName, bool succeeded, int minutesAgo = 5,
        string trigger = "Schedule", string? message = null) => new()
    {
        Id = feedId * 100 + minutesAgo,
        FeedId = feedId,
        FeedName = feedName,
        StartedUtc = DateTime.UtcNow.AddMinutes(-minutesAgo),
        FinishedUtc = DateTime.UtcNow.AddMinutes(-minutesAgo).AddSeconds(30),
        Succeeded = succeeded,
        RecordCount = succeeded ? 1200 : 0,
        Imported = succeeded ? 1200 : 0,
        TriggeredBy = trigger,
        Message = message ?? (succeeded ? "Imported 1200 of 1200; 0 delisted." : "Failed: connection refused")
    };

    private IAdminDataService _data = default!;

    private IRenderedComponent<Feeds> Render(
        IReadOnlyList<DistributorFeedView> feeds,
        IReadOnlyList<FeedSyncLogView>? history = null,
        IReadOnlyList<StaleFeedView>? stale = null)
    {
        Services.AddSingleton(Substitute.For<IToastService>());

        _data = Substitute.For<IAdminDataService>();
        _data.Feeds.Returns(feeds);
        _data.GetRecentFeedHistory().Returns(history ?? []);
        _data.GetStaleFeeds().Returns(stale ?? []);
        Services.AddSingleton(_data);

        return RenderComponent<Feeds>();
    }

    [Fact]
    public void NoBannerWhenEveryFeedLastSucceeded()
    {
        var cut = Render(
            [Feed(1, "Main"), Feed(2, "Secondary")],
            [Attempt(1, "Main", succeeded: true), Attempt(2, "Secondary", succeeded: true)]);

        cut.FindAll(".alert--danger").Should().BeEmpty();
        cut.FindAll(".alert--warn").Should().BeEmpty();
    }

    [Fact]
    public void AFeedWhoseLastAttemptFailedIsNamedInTheBanner()
    {
        var cut = Render(
            [Feed(1, "Main"), Feed(2, "Secondary")],
            [Attempt(1, "Main", succeeded: true), Attempt(2, "Secondary", succeeded: false)]);

        var banner = cut.Find(".alert--danger");

        banner.TextContent.Should().Contain("Secondary");
        banner.TextContent.Should().NotContain("Main", "that feed's last attempt imported");
    }

    [Fact]
    public void OnlyTheNewestAttemptPerFeedDecidesWhetherItIsFailing()
    {
        // A feed that failed at 03:00 and was fixed by hand at 09:00 is not failing. Reading
        // any row but the newest would leave the banner accusing a feed that works — and an
        // operator who has cleared the problem and still sees the warning stops reading it.
        var cut = Render(
            [Feed(1, "Main")],
            [
                Attempt(1, "Main", succeeded: false, minutesAgo: 360),
                Attempt(1, "Main", succeeded: true, minutesAgo: 5, trigger: "Operator")
            ]);

        cut.FindAll(".alert--danger").Should().BeEmpty();
    }

    [Fact]
    public void AStaleFeedGetsItsOwnBannerSayingNothingFailed()
    {
        var cut = Render(
            [Feed(1, "Main")],
            history: [],
            stale: [new StaleFeedView { Id = 1, Name = "Main", LastSyncedUtc = DateTime.UtcNow.AddDays(-4) }]);

        var banner = cut.Find(".alert--warn");

        banner.TextContent.Should().Contain("Main");
        // The distinction the two banners exist to draw: an attempt that broke is a different
        // problem from an attempt nobody made, and they have different first moves.
        banner.TextContent.Should().Contain("Nothing failed");
    }

    [Fact]
    public void ABannerIsNeverRenderedFromDataTheApiDidNotReturn()
    {
        // The substitute returns nothing for either read, which is what a failing API looks
        // like from here. CLAUDE.md: nothing in the portal may invent data — and a warning
        // nobody can act on reads as current, which is worse than no warning.
        var cut = Render([Feed(1, "Main")]);

        cut.FindAll(".alert--danger").Should().BeEmpty();
        cut.FindAll(".alert--warn").Should().BeEmpty();
    }

    [Fact]
    public void TheSyncButtonIsDisabledWhileThatFeedHoldsTheClaim()
    {
        var cut = Render([Feed(1, "Main", syncStarted: DateTime.UtcNow.AddMinutes(-2))]);

        var button = cut.Find("button.feed-sync");
        button.GetAttribute("title").Should().Be("Syncing…");

        // The claim in the database is the authority; this only stops a round trip the API
        // would answer with a 409.
        button.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task OpeningHistoryReadsThatFeedsAttempts()
    {
        var cut = Render([Feed(1, "Main"), Feed(2, "Secondary")]);

        _data.GetFeedHistory(2).Returns([Attempt(2, "Secondary", succeeded: false)]);

        var row = cut.FindAll("table.tbl tbody tr")
            .Single(tr => tr.TextContent.Contains("Secondary"));

        await cut.InvokeAsync(() =>
            row.QuerySelector("button.feed-history")!.Click());

        await _data.Received(1).GetFeedHistory(2);

        // Per feed, not the whole store's history: this is the same asynchronous-per-entity
        // shape GetContacts and GetDocuments use, and for the same reason.
        await _data.DidNotReceive().GetFeedHistory(1);
    }

    [Fact]
    public async Task AFeedWithNoRecordedAttemptsSaysSoRatherThanShowingAnEmptyGrid()
    {
        var cut = Render([Feed(1, "Main")]);

        _data.GetFeedHistory(1).Returns([]);

        var row = cut.Find("table.tbl tbody tr");

        await cut.InvokeAsync(() =>
            row.QuerySelector("button.feed-history")!.Click());

        // "Never attempted" and "attempted, nothing recorded" look identical in a blank grid,
        // and only one of them can happen.
        cut.Find(".empty-state").TextContent.Should().Contain("No attempts recorded");
    }

    [Fact]
    public async Task HistoryShowsWhatTriggeredEachAttempt()
    {
        var cut = Render([Feed(1, "Main")]);

        _data.GetFeedHistory(1).Returns([
            Attempt(1, "Main", succeeded: true, minutesAgo: 5, trigger: "Operator"),
            Attempt(1, "Main", succeeded: false, minutesAgo: 400, trigger: "Schedule")
        ]);

        var row = cut.Find("table.tbl tbody tr");

        await cut.InvokeAsync(() =>
            row.QuerySelector("button.feed-history")!.Click());

        // A feed that only ever succeeds when somebody presses the button is a scheduler
        // problem rather than a feed problem, and this column is the only thing that says so.
        var modal = cut.Find(".modal");
        modal.TextContent.Should().Contain("Operator");
        modal.TextContent.Should().Contain("Schedule");
    }

    [Fact]
    public void EveryRowActionIsAnIconThatSaysWhatItDoes()
    {
        var cut = Render([Feed(1, "Main")]);

        var buttons = cut.Find("table.tbl tbody tr").QuerySelectorAll("button");

        buttons.Select(button => button.GetAttribute("title")).Should().Equal(
            "Edit feed", "Sync history", "Sync now", "Delete feed");
        buttons.Select(button => button.GetAttribute("aria-label")).Should().Equal(
            "Edit Main", "Sync history of Main", "Sync Main now", "Delete Main");
        buttons.Should().OnlyContain(button => button.TextContent.Trim() == string.Empty);
    }

    [Fact]
    public async Task ATestResultIsScrolledIntoViewOnce()
    {
        var cut = Render([Feed(1, "Main")]);
        _data.TestFeed(default!).ReturnsForAnyArgs(new FeedSyncOutcome { Succeeded = true, RecordCount = 3 });

        // Completed, not just set up: the component awaits the call, and a pending one would
        // hang the click and every test scheduled after it.
        var scroll = JSInterop.SetupVoid("smportal.scrollIntoView", _ => true);
        scroll.SetVoidResult();

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent == "Add feed").Click());
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent == "Test connection").Click());

        cut.Find(".feed-test-result").TextContent.Should().Contain("Connection succeeded");
        scroll.Invocations.Should().ContainSingle();

        cut.Render();
        scroll.Invocations.Should().ContainSingle("the same result is not scrolled to again on every render");
    }
}
