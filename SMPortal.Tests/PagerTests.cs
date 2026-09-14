using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SMPortal.Shared.Admin;
using Xunit;

namespace SMPortal.Tests;

/// <summary>
/// The parent does the slicing (Skip/Take); Pager only owns the rows-per-page control and the
/// numbered buttons, windowed around the current page with gaps so a long list does not render
/// hundreds of them. These tests pin that windowing at its boundaries, since an off-by-one there
/// is invisible in the middle of a list and only shows up on the first or last page.
/// </summary>
public class PagerTests : TestContext
{
    private static IReadOnlyList<string> Sequence(IRenderedComponent<Pager> cut) =>
        cut.Find(".pager-pages").Children.Select(e => e.TextContent.Trim()).ToList();

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)] // exactly the smallest page-size option: still not worth paging
    public void RendersNothingWhileTheListFitsOnOnePageOfTheSmallestSize(int totalCount)
    {
        var cut = RenderComponent<Pager>(p => p.Add(x => x.TotalCount, totalCount));

        cut.Markup.Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public void RendersAsSoonAsTheListPassesTheSmallestPageSize()
    {
        var cut = RenderComponent<Pager>(p => p.Add(x => x.TotalCount, 11));

        cut.FindAll(".pager").Should().ContainSingle();
    }

    [Fact]
    public void WindowsTheFirstPageWithOnlyATrailingGap()
    {
        var cut = RenderComponent<Pager>(p => p.Add(x => x.TotalCount, 100).Add(x => x.PageSize, 10).Add(x => x.Page, 1));

        Sequence(cut).Should().Equal("‹", "1", "2", "…", "10", "›");
        cut.Find(".pager-btn[aria-label='Previous page']").IsDisabled().Should().BeTrue();
        cut.Find(".pager-btn[aria-label='Next page']").IsDisabled().Should().BeFalse();
    }

    [Fact]
    public void WindowsAMiddlePageWithAGapOnBothSides()
    {
        var cut = RenderComponent<Pager>(p => p.Add(x => x.TotalCount, 100).Add(x => x.PageSize, 10).Add(x => x.Page, 5));

        Sequence(cut).Should().Equal("‹", "1", "…", "4", "5", "6", "…", "10", "›");
    }

    [Fact]
    public void WindowsTheLastPageWithOnlyALeadingGap()
    {
        var cut = RenderComponent<Pager>(p => p.Add(x => x.TotalCount, 100).Add(x => x.PageSize, 10).Add(x => x.Page, 10));

        Sequence(cut).Should().Equal("‹", "1", "…", "9", "10", "›");
        cut.Find(".pager-btn[aria-label='Next page']").IsDisabled().Should().BeTrue();
        cut.Find(".pager-btn[aria-label='Previous page']").IsDisabled().Should().BeFalse();
    }

    [Fact]
    public void CollapsesTheGapWhenTheWindowAlreadyTouchesTheEnd()
    {
        // At page 2 of 10, the -1/0/+1 window (1,2,3) already abuts the first page, so there is
        // only one gap, not two either side of a lone middle number.
        var cut = RenderComponent<Pager>(p => p.Add(x => x.TotalCount, 100).Add(x => x.PageSize, 10).Add(x => x.Page, 2));

        Sequence(cut).Should().Equal("‹", "1", "2", "3", "…", "10", "›");
    }

    [Fact]
    public async Task ClickingAPageNumberReportsThatPageToTheParent()
    {
        int? changedTo = null;
        var cut = RenderComponent<Pager>(p => p
            .Add(x => x.TotalCount, 100)
            .Add(x => x.PageSize, 10)
            .Add(x => x.Page, 1)
            .Add(x => x.PageChanged, (int newPage) => changedTo = newPage));

        var lastPageButton = cut.FindAll(".pager-btn").Single(b => b.TextContent.Trim() == "10");
        await lastPageButton.ClickAsync(new MouseEventArgs());

        changedTo.Should().Be(10);
    }

    [Fact]
    public async Task ClickingPreviousAtTheFirstPageReportsNothingBecauseThereIsNowhereToGo()
    {
        var changed = false;
        var cut = RenderComponent<Pager>(p => p
            .Add(x => x.TotalCount, 100)
            .Add(x => x.PageSize, 10)
            .Add(x => x.Page, 1)
            .Add(x => x.PageChanged, (int _) => changed = true));

        await cut.Find(".pager-btn[aria-label='Previous page']").ClickAsync(new MouseEventArgs());

        changed.Should().BeFalse("GoTo clamps 1 - 1 back to 1, which already equals Page, so nothing is reported");
    }

    [Fact]
    public async Task ChangingRowsPerPagePullsAPageThatNoLongerExistsBackToTheNewLastPage()
    {
        int? newSize = null;
        int? newPage = null;
        var cut = RenderComponent<Pager>(p => p
            .Add(x => x.TotalCount, 45)
            .Add(x => x.PageSize, 10)
            .Add(x => x.Page, 5) // the last page at 10/page (45 rows -> 5 pages)
            .Add(x => x.PageSizeChanged, (int size) => newSize = size)
            .Add(x => x.PageChanged, (int page) => newPage = page));

        await cut.Find(".pager-size select").ChangeAsync(new ChangeEventArgs { Value = "25" });

        newSize.Should().Be(25);
        newPage.Should().Be(2, "45 rows at 25/page is only 2 pages, and page 5 no longer exists");
    }
}
