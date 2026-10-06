using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Sites;

/// <summary>
/// Most of the API has nothing to do with storefronts and must keep working with no
/// X-Site-Key header at all — see the remarks on AdminSiteResolutionMiddleware. Resolution here
/// is therefore best-effort, and the one thing this middleware must get right is turning a
/// downstream read of an unresolved Site into a 400 instead of an unhandled exception.
/// </summary>
public class AdminSiteResolutionMiddlewareTests
{
    private const string UserId = "staff-1";

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ISiteData _sites = Substitute.For<ISiteData>();
    private readonly IUserData _users = Substitute.For<IUserData>();

    public AdminSiteResolutionMiddlewareTests()
    {
        // Everything above store access assumes a caller who may act for any store; the tests
        // at the end are about the ones who may not.
        _users.CanActForSite(UserId, Arg.Any<int>()).Returns(true);
    }

    private static DefaultHttpContext HttpContext(string? siteKeyHeader = null, string? userId = UserId)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"));
        }

        if (siteKeyHeader is not null)
        {
            context.Request.Headers[AdminSiteResolutionMiddleware.HeaderName] = siteKeyHeader;
        }

        return context;
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    private AdminSiteResolutionMiddleware Middleware(RequestDelegate next) =>
        new(next, _cache, NullLogger<AdminSiteResolutionMiddleware>.Instance);

    [Fact]
    public async Task TurnsASiteNotResolvedExceptionFromThePipelineIntoA400()
    {
        // No header, and no single active site to fall back to, so the request stays
        // unresolved — exactly what makes reading .Site further down the pipeline throw.
        _sites.GetSites().Returns(new List<SiteModel>());

        var siteContext = new AdminSiteContext();
        var context = HttpContext();

        Task Next(HttpContext httpContext)
        {
            _ = siteContext.Site;
            return Task.CompletedTask;
        }

        await Middleware(Next).InvokeAsync(context, siteContext, _sites, _users);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        // WriteAsJsonAsync appends "; charset=utf-8" itself; the middleware only states the
        // media type.
        context.Response.ContentType.Should().StartWith("application/json");
        ReadBody(context).Should().Contain("did not name a store");
    }

    [Fact]
    public async Task PropagatesAnyOtherExceptionRatherThanTurningItIntoA400()
    {
        // The catch here is scoped to SiteNotResolvedException specifically — see the remarks
        // on AdminSiteResolutionMiddleware.InvokeAsync. Anything else is a real bug and must
        // surface as one rather than being reported as "no store named".
        _sites.GetSites().Returns(new List<SiteModel>());

        var siteContext = new AdminSiteContext();
        var context = HttpContext();

        Task Next(HttpContext _) => throw new InvalidOperationException("unrelated failure");

        var act = () => Middleware(Next).InvokeAsync(context, siteContext, _sites, _users);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolvesTheSingleActiveSiteWhenNoHeaderIsSent()
    {
        // A database with exactly one store has only one possible answer — see the remarks on
        // Resolve.
        var onlySite = new SiteModel { Id = 1, SiteKey = "only-store", IsActive = true };
        _sites.GetSites().Returns(new List<SiteModel> { onlySite });

        var siteContext = new AdminSiteContext();
        var context = HttpContext();
        string? seenKey = null;

        Task Next(HttpContext _)
        {
            seenKey = siteContext.Site.SiteKey;
            return Task.CompletedTask;
        }

        await Middleware(Next).InvokeAsync(context, siteContext, _sites, _users);

        seenKey.Should().Be("only-store");
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task ResolvesTheSiteNamedByTheHeaderWhenMultipleStoresExist()
    {
        var storeB = new SiteModel { Id = 2, SiteKey = "store-b", IsActive = true };
        _sites.GetSiteByKey("store-b").Returns(storeB);

        var siteContext = new AdminSiteContext();
        var context = HttpContext(siteKeyHeader: "store-b");
        string? seenKey = null;

        Task Next(HttpContext _)
        {
            seenKey = siteContext.Site.SiteKey;
            return Task.CompletedTask;
        }

        await Middleware(Next).InvokeAsync(context, siteContext, _sites, _users);

        seenKey.Should().Be("store-b");
    }

    [Fact]
    public async Task LeavesTheSiteUnresolvedWhenMultipleActiveStoresExistAndNoHeaderIsSent()
    {
        // A guess is safe with one store and wrong with two — see the remarks on Resolve. This
        // is the ambiguous case the header exists to disambiguate; leaving it unresolved is
        // what turns into the 400 above rather than into a guess.
        _sites.GetSites().Returns(new List<SiteModel>
        {
            new() { Id = 1, SiteKey = "store-a", IsActive = true },
            new() { Id = 2, SiteKey = "store-b", IsActive = true }
        });

        var siteContext = new AdminSiteContext();
        var context = HttpContext();

        Task Next(HttpContext httpContext)
        {
            _ = siteContext.Site;
            return Task.CompletedTask;
        }

        await Middleware(Next).InvokeAsync(context, siteContext, _sites, _users);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task IgnoresAnInactiveSiteNamedByTheHeader()
    {
        var inactive = new SiteModel { Id = 3, SiteKey = "closed-store", IsActive = false };
        _sites.GetSiteByKey("closed-store").Returns(inactive);

        var siteContext = new AdminSiteContext();
        var context = HttpContext(siteKeyHeader: "closed-store");

        Task Next(HttpContext httpContext)
        {
            _ = siteContext.Site;
            return Task.CompletedTask;
        }

        await Middleware(Next).InvokeAsync(context, siteContext, _sites, _users);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    // ---- Store access (T9) -------------------------------------------------------------------

    private async Task<(HttpContext Context, string? SeenKey)> Request(string? siteKeyHeader, string? userId = UserId)
    {
        var siteContext = new AdminSiteContext();
        var context = HttpContext(siteKeyHeader, userId);
        string? seenKey = null;

        Task Next(HttpContext _)
        {
            seenKey = siteContext.Site.SiteKey;
            return Task.CompletedTask;
        }

        await Middleware(Next).InvokeAsync(context, siteContext, _sites, _users);

        return (context, seenKey);
    }

    [Fact]
    public async Task AStoreTheCallerWasNotGivenIsAnsweredLikeAStoreThatDoesNotExist()
    {
        _sites.GetSiteByKey("store-b").Returns(new SiteModel { Id = 2, SiteKey = "store-b", IsActive = true });
        _users.CanActForSite("limited", 2).Returns(false);

        var refused = await Request("store-b", userId: "limited");
        var unknown = await Request("no-such-store", userId: "limited");

        refused.SeenKey.Should().BeNull();
        refused.Context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        // The same words for both, so the answer says nothing about which stores exist.
        ReadBody(refused.Context).Should().Be(ReadBody(unknown.Context));
    }

    [Fact]
    public async Task AStoreTheCallerWasGivenResolves()
    {
        _sites.GetSiteByKey("store-b").Returns(new SiteModel { Id = 2, SiteKey = "store-b", IsActive = true });
        _users.CanActForSite("limited", 2).Returns(true);

        var (_, seenKey) = await Request("store-b", userId: "limited");

        seenKey.Should().Be("store-b");
    }

    [Fact]
    public async Task TakingAStoreAwayAppliesToTheVeryNextRequest()
    {
        // The site row is cached for a minute; access must not be, or a revoked admin keeps
        // working for that minute — and for as long as the token lives if it were a claim.
        _sites.GetSiteByKey("store-b").Returns(new SiteModel { Id = 2, SiteKey = "store-b", IsActive = true });
        _users.CanActForSite("limited", 2).Returns(true, false);

        (await Request("store-b", userId: "limited")).SeenKey.Should().Be("store-b");
        (await Request("store-b", userId: "limited")).SeenKey.Should().BeNull();
    }

    [Fact]
    public async Task TheSingleStoreFallbackStillAsksWhetherTheCallerMayActForIt()
    {
        _sites.GetSites().Returns(new List<SiteModel> { new() { Id = 1, SiteKey = "only-store", IsActive = true } });
        _users.CanActForSite("limited", 1).Returns(false);

        var (context, seenKey) = await Request(siteKeyHeader: null, userId: "limited");

        seenKey.Should().BeNull();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task AnAnonymousCallerActsForNoStore()
    {
        _sites.GetSiteByKey("store-b").Returns(new SiteModel { Id = 2, SiteKey = "store-b", IsActive = true });

        var (_, seenKey) = await Request("store-b", userId: null);

        seenKey.Should().BeNull();
        _users.DidNotReceiveWithAnyArgs().CanActForSite(default!, default);
    }
}
