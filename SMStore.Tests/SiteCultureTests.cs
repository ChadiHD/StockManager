using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Sites;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// A request runs in its store's culture (T10). Nothing set one before, so a date took whatever
/// the container's LANG said, and a Brazilian store would have written English month names.
/// </summary>
public class SiteCultureTests
{
    private static async Task<(string Culture, string UiCulture)> CultureSeenDownstream(string locale)
    {
        var sites = Substitute.For<ISiteData>();
        sites.GetSiteByDomain("shop.test").Returns(new SiteModel { Id = 1, SiteKey = "shop", Domain = "shop.test", Locale = locale });

        var services = new ServiceCollection().AddSingleton(sites).BuildServiceProvider();
        var resolver = new SiteResolver(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new SiteOptions()),
            NullLogger<SiteResolver>.Instance);

        (string, string) seen = default;
        var middleware = new SiteResolutionMiddleware(_ =>
        {
            seen = (CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name);
            return Task.CompletedTask;
        }, resolver);

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("shop.test");

        await middleware.InvokeAsync(context, new SiteContext());

        return seen;
    }

    [Fact]
    public async Task EverythingAfterResolutionRunsInTheStoresLocale()
    {
        (await CultureSeenDownstream("pt-BR")).Should().Be(("pt-BR", "pt-BR"));
        (await CultureSeenDownstream("en-IE")).Should().Be(("en-IE", "en-IE"));
    }

    [Fact]
    public async Task ALocaleNobodyKnowsKeepsTheServersCultureRatherThanFailingTheRequest()
    {
        var before = CultureInfo.CurrentCulture.Name;

        (await CultureSeenDownstream("xx-NOWHERE")).Culture.Should().Be(before);
    }
}
