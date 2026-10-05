using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using StockApi.Scheduling;
using Xunit;

namespace StockApi.Tests.Scheduling;

/// <summary>
/// The cutoffs the nightly sweep hands the procedure. What the procedure deletes is held
/// against the database by <c>HousekeepingTests</c>.
/// </summary>
public class HousekeepingBackgroundServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 3, 30, 0, DateTimeKind.Utc);

    private readonly IHousekeepingData _data = Substitute.For<IHousekeepingData>();

    private HousekeepingBackgroundService Build(params (string Key, string Value)[] settings)
    {
        _data.Sweep(default, default).ReturnsForAnyArgs(new HousekeepingResult(0, 0));

        var services = new ServiceCollection();
        services.AddSingleton(_data);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value))
            .Build();

        return new HousekeepingBackgroundService(
            services.BuildServiceProvider(), config, NullLogger<HousekeepingBackgroundService>.Instance);
    }

    [Fact]
    public void TheDefaultsAreAMonthForBasketsAndAQuarterForMail()
    {
        Build().Sweep(Now);

        _data.Received(1).Sweep(Now.AddDays(-30), Now.AddDays(-90));
    }

    [Fact]
    public void TheWindowsComeFromConfiguration()
    {
        Build(("Housekeeping:AbandonedBasketDays", "7"), ("Housekeeping:SentMailDays", "14")).Sweep(Now);

        _data.Received(1).Sweep(Now.AddDays(-7), Now.AddDays(-14));
    }

    [Theory]
    [InlineData("0", "90")]
    [InlineData("30", "-1")]
    public void AWindowUnderADayIsRefusedRatherThanDeletingLiveBaskets(string basketDays, string mailDays)
    {
        Build(("Housekeeping:AbandonedBasketDays", basketDays), ("Housekeeping:SentMailDays", mailDays)).Sweep(Now);

        _data.DidNotReceiveWithAnyArgs().Sweep(default, default);
    }

    [Fact]
    public void AFailedSweepEndsThePassRatherThanTheService()
    {
        var service = Build();
        _data.Sweep(default, default).ReturnsForAnyArgs(_ => throw new InvalidOperationException("deadlock"));

        var sweep = () => service.Sweep(Now);

        sweep.Should().NotThrow();
    }
}
