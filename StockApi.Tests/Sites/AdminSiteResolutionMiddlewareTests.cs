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
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ISiteData _sites = Substitute.For<ISiteData>();

    private static DefaultHttpContext HttpContext(string? siteKeyHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

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

        await Middleware(Next).InvokeAsync(context, siteContext, _sites);

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

        var act = () => Middleware(Next).InvokeAsync(context, siteContext, _sites);

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

        await Middleware(Next).InvokeAsync(context, siteContext, _sites);

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

        await Middleware(Next).InvokeAsync(context, siteContext, _sites);

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

        await Middleware(Next).InvokeAsync(context, siteContext, _sites);

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

        await Middleware(Next).InvokeAsync(context, siteContext, _sites);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
