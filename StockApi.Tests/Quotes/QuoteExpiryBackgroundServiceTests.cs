using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Quotes;
using Xunit;

namespace StockApi.Tests.Quotes;

/// <summary>
/// The nightly pass over every store. Which quotes it picks is the procedure's, and
/// <c>QuoteExpiryTests</c> holds that against the database; this is the loop around it.
/// </summary>
public class QuoteExpiryBackgroundServiceTests
{
    private readonly IQuoteData _quotes = Substitute.For<IQuoteData>();

    private static SiteModel Site(int id, bool active = true) =>
        new() { Id = id, SiteKey = $"site-{id}", IsActive = active };

    private QuoteExpiryBackgroundService Build(ISiteData sites, params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sites);
        services.AddSingleton(_quotes);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value))
            .Build();

        return new QuoteExpiryBackgroundService(
            services.BuildServiceProvider(), config, NullLogger<QuoteExpiryBackgroundService>.Instance);
    }

    private static ISiteData Sites(params SiteModel[] sites)
    {
        var data = Substitute.For<ISiteData>();
        data.GetSites().Returns(sites.ToList());
        return data;
    }

    [Fact]
    public void EveryActiveStoreIsSweptAndInactiveOnesAreNot()
    {
        Build(Sites(Site(1), Site(2, active: false), Site(3))).SweepEverySite(CancellationToken.None);

        _quotes.Received(1).QueueExpiryNotices(1, 3);
        _quotes.Received(1).QueueExpiryNotices(3, 3);
        // An inactive store's storefront answers nothing; a reminder would point at a 404.
        _quotes.DidNotReceive().QueueExpiryNotices(2, Arg.Any<int>());
    }

    [Fact]
    public void TheWindowComesFromConfiguration()
    {
        Build(Sites(Site(1)), ("Quotes:ExpiryNoticeDays", "5")).SweepEverySite(CancellationToken.None);

        _quotes.Received(1).QueueExpiryNotices(1, 5);
    }

    [Fact]
    public void OneStoresFailureDoesNotCostAnotherItsReminders()
    {
        _quotes.QueueExpiryNotices(1, Arg.Any<int>()).Returns(_ => throw new InvalidOperationException("deadlock"));

        Build(Sites(Site(1), Site(3))).SweepEverySite(CancellationToken.None);

        _quotes.Received(1).QueueExpiryNotices(3, 3);
    }

    [Fact]
    public void ASiteListThatCannotBeReadEndsThePassRatherThanTheService()
    {
        var sites = Substitute.For<ISiteData>();
        sites.GetSites().Returns(_ => throw new InvalidOperationException("database asleep"));

        var sweep = () => Build(sites).SweepEverySite(CancellationToken.None);

        sweep.Should().NotThrow();
        _quotes.DidNotReceiveWithAnyArgs().QueueExpiryNotices(default, default);
    }

    [Fact]
    public async Task ItIsOffUnlessSomebodyTurnsItOn()
    {
        // The one job that writes to customers with nobody having done anything, against a
        // development database that is usually a copy of a real one.
        var service = Build(Sites(Site(1)));

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        _quotes.DidNotReceiveWithAnyArgs().QueueExpiryNotices(default, default);
    }
}
