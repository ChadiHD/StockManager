using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Email;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Controllers;

/// <summary>
/// A store's own email wording, edited from the portal since T9. The dispatcher already refused a
/// broken row at send time — and sent the platform's words with a warning nobody reads. These
/// hold that the same refusal now happens when somebody can still fix it.
/// </summary>
public class EmailWordingTests
{
    private const int SiteId = 4;

    private readonly ISiteEmailTemplateData _wording = Substitute.For<ISiteEmailTemplateData>();
    private readonly SiteEmailTemplateController _controller;

    public EmailWordingTests()
    {
        var site = Substitute.For<IAdminSiteContext>();
        site.SiteId.Returns(SiteId);
        site.Site.Returns(new SiteModel { Id = SiteId, Name = "Acme", Domain = "shop.example.com" });

        _controller = new SiteEmailTemplateController(_wording, site);
    }

    [Fact]
    public void APlaceholderTheMessageHasNoValueForIsRefusedOnSave()
    {
        // It would arrive in the customer's inbox as {Compnay}.
        var result = _controller.Put(EmailTemplates.AccountApproved.Key,
            new SiteEmailTemplateController.EmailWordingEdit(null, "Welcome aboard, {Compnay}."));

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<string>().Which.Should().Contain("{Compnay}");
        _wording.DidNotReceiveWithAnyArgs().Save(default, default!, default!, default!);
    }

    [Fact]
    public void ARequiredPlaceholderLeftOutIsRefusedOnSave()
    {
        // A reset mail with no link is a dead end for the customer who asked for it.
        var result = _controller.Put(EmailTemplates.PasswordReset.Key,
            new SiteEmailTemplateController.EmailWordingEdit("Reset your password", "Click the link we sent you."));

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<string>().Which.Should().Contain("{ResetLink}");
    }

    [Fact]
    public void GoodWordingIsSavedForTheActingStore()
    {
        var result = _controller.Put(EmailTemplates.AccountApproved.Key,
            new SiteEmailTemplateController.EmailWordingEdit(" Welcome to {SiteName} ", "Hello {Company}, sign in at {SignInLink}."));

        result.Value!.Customised.Should().BeTrue();
        _wording.Received(1).Save(SiteId, EmailTemplates.AccountApproved.Key,
            "Welcome to {SiteName}", "Hello {Company}, sign in at {SignInLink}.");
    }

    [Fact]
    public void BlankingBothHalvesGoesBackToThePlatformsWording()
    {
        var result = _controller.Put(EmailTemplates.AccountApproved.Key,
            new SiteEmailTemplateController.EmailWordingEdit(" ", ""));

        result.Value!.Customised.Should().BeFalse();
        _wording.Received(1).Delete(SiteId, EmailTemplates.AccountApproved.Key);
        _wording.DidNotReceiveWithAnyArgs().Save(default, default!, default!, default!);
    }

    [Fact]
    public void EveryMessageIsListedWithWhatItCanFillAndMustKeep()
    {
        var views = _controller.Get().ToList();

        views.Select(view => view.Key).Should().Equal(EmailTemplates.All.Select(template => template.Key));

        var reset = views.Single(view => view.Key == EmailTemplates.PasswordReset.Key);
        reset.Required.Should().Contain("ResetLink");
        reset.Placeholders.Should().Contain(["ResetLink", "SiteName", "SiteDomain"]);
    }

    [Fact]
    public void AnUnknownMessageIsNotFound()
    {
        _controller.Put("no.such.message", new SiteEmailTemplateController.EmailWordingEdit("x", "y"))
            .Result.Should().BeOfType<NotFoundResult>();
    }
}
