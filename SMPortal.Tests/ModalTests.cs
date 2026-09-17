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
    public async Task ClickingTheBackdropInvokesOnClose()
    {
        var closed = false;
        var cut = RenderComponent<Modal>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnClose, () => closed = true));

        await cut.Find(".modal-backdrop").ClickAsync(new MouseEventArgs());

        closed.Should().BeTrue();
    }

    [Fact]
    public async Task ClickingInsideTheModalCannotReachTheBackdropsCloseHandler()
    {
        // @onclick:stopPropagation on .modal stops the click from bubbling to the backdrop's
        // OnClose at all — bUnit models that by refusing to dispatch the event any further
        // rather than quietly delivering it nowhere, which is itself proof nothing downstream
        // (here, OnClose) can fire from a click on the form.
        var closed = false;
        var cut = RenderComponent<Modal>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnClose, () => closed = true)
            .AddChildContent("<p class=\"probe\">Body</p>"));

        var clickInsideTheModal = () => cut.Find(".probe").ClickAsync(new MouseEventArgs());

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
