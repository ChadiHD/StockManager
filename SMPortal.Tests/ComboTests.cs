using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SMPortal.Shared.Admin;
using Xunit;

namespace SMPortal.Tests;

/// <summary>
/// Combo is the searchable-select CLAUDE.md singles out for its own trap: once an operator has
/// typed, a live browser's &lt;input&gt; stops following the value attribute Blazor writes, so
/// picking a row can leave the old query on screen. The fix re-keys the input on every
/// programmatic text change via <c>@key="_textStamp"</c>.
///
/// Neither half of that bug is directly observable through bUnit: AngleSharp does not model a
/// real browser's dirty-value quirk (an attribute write always "wins" there, key or no key), and
/// <c>cut.Find(...)</c> hands back a fresh wrapper object on every call regardless of whether the
/// underlying element was patched or recreated, so element identity is not a usable proxy for
/// "did @key force a new node" either — both were tried while writing these tests, and the
/// second one fails even against the current, correct markup. What is left, and what these tests
/// pin instead, is the functional contract @key exists to protect: a query typed after opening
/// the box is fully replaced by the picked row's label, never left showing stale text.
/// </summary>
public class ComboTests : TestContext
{
    private static readonly ComboOption[] Options =
    {
        new("a", "Alpha"),
        new("b", "Beta"),
    };

    private static IHtmlInputElement Input(IRenderedComponent<Combo> cut) =>
        (IHtmlInputElement)cut.Find("input.combo-input");

    [Fact]
    public void ShowsTheSelectedOptionsLabelWhenClosed()
    {
        var cut = RenderComponent<Combo>(p => p.Add(x => x.Options, Options).Add(x => x.Value, "a"));

        Input(cut).Value.Should().Be("Alpha");
    }

    [Fact]
    public async Task FocusingClearsTheLabelSoTheWholeListIsSearchable()
    {
        var cut = RenderComponent<Combo>(p => p.Add(x => x.Options, Options).Add(x => x.Value, "a"));

        await Input(cut).FocusInAsync(new FocusEventArgs());

        Input(cut).Value.Should().BeEmpty();
        cut.FindAll(".combo-option").Should().HaveCount(Options.Length, "the full list shows until a query narrows it");
    }

    [Fact]
    public async Task PickingAnOptionAfterTypingReplacesTheQueryWithTheOptionsLabel()
    {
        string? picked = "not set";
        var cut = RenderComponent<Combo>(p => p
            .Add(x => x.Options, Options)
            .Add(x => x.Value, "a")
            .Add(x => x.ValueChanged, (string? v) => picked = v));

        await Input(cut).FocusInAsync(new FocusEventArgs());
        await Input(cut).InputAsync(new ChangeEventArgs { Value = "bet" });

        var option = cut.FindAll(".combo-option").Single(o => o.TextContent.Contains("Beta"));
        await option.ClickAsync(new MouseEventArgs());

        picked.Should().Be("b");
        Input(cut).Value.Should().Be("Beta", "the typed query must not survive the pick");
    }

    [Fact]
    public async Task EscapeCancelsAndRestoresThePreviouslySelectedLabelRatherThanKeepingATypedQuery()
    {
        var cut = RenderComponent<Combo>(p => p.Add(x => x.Options, Options).Add(x => x.Value, "a"));

        await Input(cut).FocusInAsync(new FocusEventArgs());
        await Input(cut).InputAsync(new ChangeEventArgs { Value = "no such option" });

        await Input(cut).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Input(cut).Value.Should().Be("Alpha");
        cut.FindAll(".combo-list").Should().BeEmpty("closing collapses the option list");
    }

    [Fact]
    public async Task FilteringRequiresEveryTypedWordAndCanSpanTheLabelAndSubLine()
    {
        var options = new[]
        {
            new ComboOption("a", "Alpha Widget"),
            new ComboOption("b", "Beta Widget"),
            new ComboOption("c", "Gamma Gadget"),
        };
        var cut = RenderComponent<Combo>(p => p.Add(x => x.Options, options).Add(x => x.Value, (string?)null));

        await Input(cut).FocusInAsync(new FocusEventArgs());
        await Input(cut).InputAsync(new ChangeEventArgs { Value = "widget alpha" });

        var shown = cut.FindAll(".combo-option");
        shown.Should().ContainSingle();
        shown[0].TextContent.Should().Contain("Alpha Widget");
    }

    [Fact]
    public async Task CapsRenderedOptionsAndReportsTheRemainderInsteadOfRenderingThousandsOfRows()
    {
        var options = Enumerable.Range(1, 120)
            .Select(i => new ComboOption(i.ToString(), $"Item {i:000}"))
            .ToArray();
        var cut = RenderComponent<Combo>(p => p.Add(x => x.Options, options).Add(x => x.MaxVisible, 50));

        await Input(cut).FocusInAsync(new FocusEventArgs());

        cut.FindAll(".combo-option").Should().HaveCount(50);
        cut.Find(".combo-more").TextContent.Should().Contain("70 more match");
    }

    [Fact]
    public async Task DisabledOptionsAreShownButCannotBePicked()
    {
        var options = new[]
        {
            new ComboOption("a", "Alpha", Meta: "In stock"),
            new ComboOption("b", "Beta", Meta: "Out of stock", Disabled: true),
        };
        string? picked = "not set";
        var cut = RenderComponent<Combo>(p => p
            .Add(x => x.Options, options)
            .Add(x => x.ValueChanged, (string? v) => picked = v));

        await Input(cut).FocusInAsync(new FocusEventArgs());
        var disabledOption = cut.FindAll(".combo-option").Single(o => o.TextContent.Contains("Beta"));

        disabledOption.IsDisabled().Should().BeTrue();

        await disabledOption.ClickAsync(new MouseEventArgs());

        picked.Should().Be("not set", "Pick() returns immediately for a disabled option");
    }
}
