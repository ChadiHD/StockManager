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
/// SearchBox only submits on Enter or the magnifier button, and only adopts a new Value from
/// its parent while the operator has not typed anything of their own. CLAUDE.md records that
/// the second half of that contract was "permanently dead" once for a different reason
/// (<c>Value="_search"</c> missing its <c>@</c> at eight call sites) — these tests exercise the
/// mechanism inside SearchBox itself, so it stays caught regardless of which call site gets it
/// wrong next.
/// </summary>
public class SearchBoxTests : TestContext
{
    private static IHtmlInputElement Input(IRenderedComponent<SearchBox> cut) =>
        (IHtmlInputElement)cut.Find("input[type=search]");

    // -- Parent sync, dead twice ----------------------------------------------------------
    //
    // These two failed when they were written, against a guard that read:
    //     if (!_dirty && _text != (Value ?? ""))
    // with _dirty defined as exactly `_text != (Value ?? "")`. `!_dirty` is therefore
    // `_text == (Value ?? "")`, the negation of the condition's own second half — "A and not
    // A", unsatisfiable for any _text or Value, so the assignment was unreachable and no Value
    // was ever adopted, not even on first render.
    //
    // Worth knowing before touching OnParametersSet again: the obvious repair, dropping the
    // second clause to leave `if (!_dirty)`, does not work either. That condition is
    // `_text == Value`, so the assignment under it is a no-op. The component needs to
    // remember what it last took from the parent, which is what _applied does now — and this
    // is the second time this component's parent sync has been silently dead, after
    // `Value="_search"` passing the literal string at eight call sites.
    [Fact]
    public void AdoptsTheValueItFirstRendersWith()
    {
        var cut = RenderComponent<SearchBox>(p => p.Add(x => x.Value, "acme"));

        Input(cut).Value.Should().Be("acme");
    }

    [Fact]
    public void AdoptsTheParentsNewValueWhenTheBoxHasNotBeenTypedInto()
    {
        var cut = RenderComponent<SearchBox>(p => p.Add(x => x.Value, "acme"));

        cut.SetParametersAndRender(p => p.Add(x => x.Value, "widget"));

        Input(cut).Value.Should().Be("widget");
    }

    [Fact]
    public async Task DoesNotOverwriteATermTheOperatorIsStillTypingWhenTheParentRendersForAnUnrelatedReason()
    {
        // Guards against the opposite mistake a naive fix could make: unconditionally copying
        // Value into _text on every parameter set would satisfy the two tests above but would
        // also let an unrelated re-render (a toast, a busy flag elsewhere on the page) blank out
        // whatever the operator is mid-typing.
        var cut = RenderComponent<SearchBox>(p => p.Add(x => x.Value, ""));

        await Input(cut).InputAsync(new ChangeEventArgs { Value = "widget" });

        cut.SetParametersAndRender(p => p.Add(x => x.Value, ""));

        Input(cut).Value.Should().Be("widget");
    }

    [Fact]
    public async Task SubmitsOnEnterOnlyWhenTheBoxHasBeenChanged()
    {
        string? submitted = null;
        var cut = RenderComponent<SearchBox>(p => p
            .Add(x => x.Value, "")
            .Add(x => x.ValueChanged, (string v) => submitted = v));

        // Untouched box, Enter pressed anyway: nothing to (re)submit.
        await Input(cut).KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        submitted.Should().BeNull();

        await Input(cut).InputAsync(new ChangeEventArgs { Value = "acme" });
        await Input(cut).KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        submitted.Should().Be("acme");
    }

    [Fact]
    public async Task NeverSubmitsWhileTyping()
    {
        // The whole point of a submit-on-Enter box: fuzzy scoring runs over the full in-memory
        // list on the single WebAssembly UI thread, so firing on every keystroke would be the
        // freeze this component exists to avoid.
        var submissions = 0;
        var cut = RenderComponent<SearchBox>(p => p
            .Add(x => x.Value, "")
            .Add(x => x.ValueChanged, (string _) => submissions++));

        var typed = "";
        foreach (var letter in "acme")
        {
            typed += letter;
            await Input(cut).InputAsync(new ChangeEventArgs { Value = typed });
        }

        submissions.Should().Be(0);
    }

    [Fact]
    public async Task ClearingAnActiveSearchNotifiesTheParentWithAnEmptyTerm()
    {
        string? changedTo = null;
        var cut = RenderComponent<SearchBox>(p => p
            .Add(x => x.Value, "")
            .Add(x => x.ValueChanged, (string v) => changedTo = v));

        // Typed but not yet submitted — Value is still "", which is what Clear checks before
        // deciding there is anything upstream worth resetting.
        await Input(cut).InputAsync(new ChangeEventArgs { Value = "widget" });
        await cut.Find(".searchbox-clear").ClickAsync(new MouseEventArgs());

        changedTo.Should().BeNull("nothing had been submitted yet, so there was nothing for the parent to clear");
        Input(cut).Value.Should().BeEmpty();

        // Now submit, then clear a term the parent actually has.
        await Input(cut).InputAsync(new ChangeEventArgs { Value = "widget" });
        await Input(cut).KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.SetParametersAndRender(p => p.Add(x => x.Value, "widget"));

        await cut.Find(".searchbox-clear").ClickAsync(new MouseEventArgs());

        changedTo.Should().Be("");
    }

    [Fact]
    public async Task EscapeClearsTheBoxLikeTheClearButton()
    {
        var cut = RenderComponent<SearchBox>(p => p.Add(x => x.Value, ""));

        await Input(cut).InputAsync(new ChangeEventArgs { Value = "widget" });
        await Input(cut).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Input(cut).Value.Should().BeEmpty();
    }

    [Fact]
    public void DisablesEveryControlWhileTheParentIsSearching()
    {
        var cut = RenderComponent<SearchBox>(p => p.Add(x => x.Value, "acme").Add(x => x.Busy, true));

        cut.Find("input[type=search]").IsDisabled().Should().BeTrue();
        cut.Find(".searchbox-go").IsDisabled().Should().BeTrue();
    }
}
