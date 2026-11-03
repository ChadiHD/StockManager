using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;
using StockApi.Controllers;
using StockApi.Feeds;
using StockApi.Sites;
using Xunit;
using FeedInput = StockApi.Controllers.DistributorFeedController.FeedInput;

namespace StockApi.Tests.Feeds;

// A stored distributor password may be sent again only to the server it was saved for. Test
// and Update both read an empty password as "use the stored one", and they used to take the
// destination from the same request, which handed the password to whoever an admin named.
public class StoredFeedPasswordTests
{
    private const string PinnedKey = "pinned-key";

    private readonly IDistributorFeedData _feeds = Substitute.For<IDistributorFeedData>();
    private readonly IDistributorFeedSyncService _sync = Substitute.For<IDistributorFeedSyncService>();
    private readonly IFeedSecretStoreResolver _secrets = Substitute.For<IFeedSecretStoreResolver>();
    private readonly IAdminSiteContext _site = Substitute.For<IAdminSiteContext>();
    private readonly DistributorFeedController _controller;

    public StoredFeedPasswordTests()
    {
        _site.SiteId.Returns(42);
        _feeds.GetFeedById(3, 42).Returns(_ => Stored());
        _sync.TestAsync(Arg.Any<DistributorFeedModel>(), Arg.Any<string>())
            .Returns(new DistributorFeedResult { Succeeded = true });
        _secrets.Active.ProviderName.Returns("DataProtection");
        _controller = new DistributorFeedController(_feeds, _sync, _secrets, _site);
    }

    [Theory]
    [InlineData("attacker.example", 22, "flexit", PinnedKey)]
    [InlineData("sftp.flexit.example", 2222, "flexit", PinnedKey)]
    [InlineData("sftp.flexit.example", 22, "someone-else", PinnedKey)]
    [InlineData("sftp.flexit.example", 22, "flexit", null)]
    [InlineData("sftp.flexit.example", 22, "flexit", "another-key")]
    public async Task TestingAnotherEndpointWithoutAPasswordIsRefused(string host, int port, string user, string? key)
    {
        var result = await _controller.Test(Input(host, port, user, key, password: ""), id: 3);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<DistributorFeedResult>()
            .Which.Error.Should().Be(FeedEndpoint.ReenterPassword);
        await _sync.DidNotReceiveWithAnyArgs().TestAsync(default!, default!);
    }

    [Fact]
    public async Task TestingTheSavedEndpointUsesTheStoredPassword()
    {
        // Host case differs: DNS does not care, so neither does this.
        await _controller.Test(Input("SFTP.flexit.example", 22, "flexit", PinnedKey, password: ""), id: 3);

        await _sync.Received(1).TestAsync(
            Arg.Is<DistributorFeedModel>(feed => feed.SecretRef == "stored-ref"), "");
    }

    [Fact]
    public async Task AnotherEndpointIsTestedWhenThePasswordIsTyped()
    {
        await _controller.Test(Input("sftp2.flexit.example", 22, "flexit", null, password: "typed"), id: 3);

        await _sync.Received(1).TestAsync(Arg.Any<DistributorFeedModel>(), "typed");
    }

    [Fact]
    public async Task PinningAKeyWhereThereWasNoneNeedsNoPassword()
    {
        _feeds.GetFeedById(3, 42).Returns(_ => Stored(hostKey: null));

        await _controller.Test(Input("sftp.flexit.example", 22, "flexit", PinnedKey, password: ""), id: 3);

        await _sync.ReceivedWithAnyArgs(1).TestAsync(default!, default!);
    }

    [Fact]
    public async Task SavingAnotherEndpointWithoutAPasswordIsRefused()
    {
        // Saved, the nightly sync would send the stored password there with nobody watching.
        var result = await _controller.Update(3, Input("attacker.example", 22, "flexit", null, password: ""));

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be(FeedEndpoint.ReenterPassword);
        _feeds.DidNotReceiveWithAnyArgs().UpdateFeed(default!, default);
    }

    [Fact]
    public async Task SavingAnotherEndpointWithAPasswordStoresBoth()
    {
        var result = await _controller.Update(3, Input("sftp2.flexit.example", 22, "flexit", PinnedKey, password: "typed"));

        result.Should().BeOfType<NoContentResult>();
        _feeds.Received(1).UpdateFeed(Arg.Is<DistributorFeedModel>(feed => feed.Host == "sftp2.flexit.example"), 42);
        await _secrets.Active.Received(1).ProtectAsync(Arg.Any<string>(), "typed");
    }

    private static DistributorFeedModel Stored(string? hostKey = PinnedKey) => new()
    {
        Id = 3,
        Name = "Flexit",
        Host = "sftp.flexit.example",
        Port = 22,
        Username = "flexit",
        HostKeySha256 = hostKey!,
        SecretProvider = "DataProtection",
        SecretRef = "stored-ref"
    };

    private static FeedInput Input(string host, int port, string user, string? hostKey, string password) => new(
        Name: "Flexit", Host: host, Port: port, Username: user, Password: password, RemoteDirectory: ".",
        HostKeySha256: hostKey, Enabled: true,
        FieldSku: null, FieldName: null, FieldDescription: null, FieldCategory: null, FieldCost: null,
        FieldSrp: null, FieldQuantity: null, FieldManufacturer: null, FieldMpn: null, FieldEan: null,
        FieldIcecat: null);
}
