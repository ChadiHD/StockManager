using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace StockManager.RateLimiting;

/// <summary>
/// What counts as one caller to a per-IP rate limit, on both hosts.
/// </summary>
/// <remarks>
/// An IPv4 address, or an IPv6 /64. One subscriber is routinely handed a whole /64, so keyed by
/// the full IPv6 address every sign-in limit here was 2^64 limits to anyone who wanted them.
/// An IPv4 client reached over a dual-stack socket arrives as an IPv4-mapped IPv6 address, and
/// is unmapped first: masked as IPv6, every IPv4 client in the world shares one /64.
///
/// The address is the connection's, which behind the ingress is the client's only because
/// ProductionTopology turns forwarded headers on. Unknown addresses share a partition rather
/// than escaping the limit.
/// </remarks>
public static class RateLimitCaller
{
    public static string Of(HttpContext context) => Of(context.Connection.RemoteIpAddress);

    public static string Of(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);

        return $"{new IPAddress(bytes)}/64";
    }
}
