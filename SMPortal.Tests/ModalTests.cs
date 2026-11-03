using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using SMPortal.Shared.Admin;
using Xunit;

namespace SMPortal.Tests;

/// <summary>Reusable modal shell: every admin dialog (approve, reject, edit terms, add-record
/// forms) is a form dropped into this as child content, so a mistake here would surface on
/// every one of them at once.</summary>
public partial class ModalTests : TestContext
{
    [Fact]
    public void RendersNothingWhenNotVisible()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(x => x.Visible, false)
            .Add(x => x.Title, "Approve account")
            .AddChildContent("<p class=\"probe\">Body</p>"));

        cut.Markup.Should().BeNullOrWhiteSpace("the guard suppresses the whole shell, not just its own chrome");
    }

    [Fact]
    public void RendersTheTitleAndChildContentWhenVisible()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Title, "Approve account")
            .AddChildContent("<p class=\"probe\">Body markup</p>"));

        cut.Find(".modal-head strong").TextContent.Should().Be("Approve account");
        cut.Find(".probe").TextContent.Should().Be("Body markup");
    }

    [Fact]
    public async Task ClickingOutsideOrInsideTheDialogDoesNotClose()
    {
        // A dialog usually holds a half-filled form, and a stray click beside it threw the
        // typing away. bUnit refuses to dispatch a click to an element with no handler, which
        // is the assertion: neither the backdrop nor the body has anything that could fire.
        var cut = RenderComponent<Modal>(p => p
            .Add(x => x.Visible, true)
            .AddChildContent("<p class=\"probe\">Body</p>"));

        var clickTheBackdrop = () => cut.Find(".modal-backdrop").ClickAsync(new MouseEventArgs());
        var clickInsideTheModal = () => cut.Find(".probe").ClickAsync(new MouseEventArgs());

        await clickTheBackdrop.Should().ThrowAsync<MissingEventHandlerException>();
        await clickInsideTheModal.Should().ThrowAsync<MissingEventHandlerException>();
    }

    [Fact]
    public void TheHeadHasNoCloseButtonOfItsOwn()
    {
        // Every dialog closes from its foot, and an X beside a Close button was two controls for
        // one thing.
        var cut = RenderComponent<Modal>(p => p.Add(x => x.Visible, true).Add(x => x.Title, "Add product"));

        cut.FindAll(".modal-head button").Should().BeEmpty();
    }

    [Fact]
    public void EveryDialogInThePortalHasAButtonInItsFoot()
    {
        // With no X and no backdrop click, a dialog whose foot has no button cannot be closed at
        // all. Read from the pages' source, because each dialog only renders behind its own
        // page's state.
        var dialogs = Directory.EnumerateFiles(PortalDirectory(), "*.razor", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(file => Dialog().Matches(File.ReadAllText(file))
                .Select(match => (File: Path.GetFileName(file), Markup: match.Value)))
            .ToList();

        dialogs.Should().HaveCountGreaterThan(15, "a regex that stopped matching would pass by finding nothing");
        dialogs.Where(dialog => !FootButton().IsMatch(dialog.Markup))
            .Select(dialog => $"{dialog.File}: {dialog.Markup[..Math.Min(80, dialog.Markup.Length)]}")
            .Should().BeEmpty();
    }

    private static string PortalDirectory([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "SMPortal", "Pages"));

    [GeneratedRegex(@"<Modal\b.*?</Modal>", RegexOptions.Singleline)]
    private static partial Regex Dialog();

    [GeneratedRegex(@"class=""modal-foot"">.*?<button type=""button""[^>]*@onclick", RegexOptions.Singleline)]
    private static partial Regex FootButton();
}
