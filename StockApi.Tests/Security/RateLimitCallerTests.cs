using System.Net;
using FluentAssertions;
using StockManager.RateLimiting;
using Xunit;

namespace StockApi.Tests.Security;

// Both hosts' per-IP limits key on this. Get it wrong one way and an IPv6 user has 2^64
// allowances; get it wrong the other and every IPv4 client shares one.
public class RateLimitCallerTests
{
    [Fact]
    public void AddressesInOneIpv6SlashSixtyFourAreOneCaller()
    {
        RateLimitCaller.Of(IPAddress.Parse("2001:db8:1:2:aaaa::1"))
            .Should().Be(RateLimitCaller.Of(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff")));

        RateLimitCaller.Of(IPAddress.Parse("2001:db8:1:2::1"))
            .Should().NotBe(RateLimitCaller.Of(IPAddress.Parse("2001:db8:1:3::1")));
    }

    [Fact]
    public void AnIpv4ClientOnADualStackSocketIsItsOwnCaller()
    {
        // Masked as IPv6, every mapped address falls in ::ffff:0:0/64.
        RateLimitCaller.Of(IPAddress.Parse("::ffff:203.0.113.7")).Should().Be("203.0.113.7");
        RateLimitCaller.Of(IPAddress.Parse("::ffff:203.0.113.8")).Should().Be("203.0.113.8");
    }

    [Fact]
    public void Ipv4AndUnknownAreKeptAsTheyAre()
    {
        RateLimitCaller.Of(IPAddress.Parse("203.0.113.7")).Should().Be("203.0.113.7");
        RateLimitCaller.Of((IPAddress?)null).Should().Be("unknown");
    }
}
