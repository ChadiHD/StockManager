using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using SMPortal.Shared.Admin;
using Xunit;

namespace SMPortal.Tests;

/// <summary>Reusable modal shell: every admin dialog (approve, reject, edit terms, add-record
/// forms) is a form dropped into this as child content, so a mistake here would surface on
/// every one of them at once.</summary>
public class ModalTests : TestContext
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
        var closed = false;
        var cut = RenderComponent<Modal>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnClose, () => closed = true)
            .AddChildContent("<p class=\"probe\">Body</p>"));

        var clickTheBackdrop = () => cut.Find(".modal-backdrop").ClickAsync(new MouseEventArgs());
        var clickInsideTheModal = () => cut.Find(".probe").ClickAsync(new MouseEventArgs());

        await clickTheBackdrop.Should().ThrowAsync<MissingEventHandlerException>();
        await clickInsideTheModal.Should().ThrowAsync<MissingEventHandlerException>();
        closed.Should().BeFalse();
    }

    [Fact]
    public async Task ClickingTheCloseButtonInvokesOnClose()
    {
        var closed = false;
        var cut = RenderComponent<Modal>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnClose, () => closed = true));

        await cut.Find(".modal-x").ClickAsync(new MouseEventArgs());

        closed.Should().BeTrue();
    }
}
