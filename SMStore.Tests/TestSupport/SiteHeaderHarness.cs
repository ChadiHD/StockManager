using Bunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Components.Shared;
using SMStore.Navigation;
using SMStore.Ordering;
using SMStore.Sites;

namespace SMStore.Tests.TestSupport;

/// <summary>
/// Wires up SiteHeader's dependency graph for a given site and ordering mode, then renders it.
/// </summary>
internal static class SiteHeaderHarness
{
    public static IRenderedComponent<SiteHeader> Render(
        Bunit.TestContext context, SiteModel site, IOrderingMode mode, ICustomerContext? customer = null)
    {
        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(site);
        siteContext.IsResolved.Returns(true);

        customer ??= Substitute.For<ICustomerContext>();

        var ordering = new OrderingModeProvider(siteContext, [mode]);

        // SiteThemeResolver is a concrete seam over IWebHostEnvironment. A NullFileProvider
        // means every asset "does not exist", so every path resolves to the shipped default
        // theme -- deterministic, and irrelevant here since no test asserts on the path.
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.WebRootFileProvider.Returns(new NullFileProvider());

        context.Services.AddSingleton(siteContext);
        context.Services.AddSingleton(customer);
        context.Services.AddSingleton(new SiteThemeResolver(environment, NullLogger<SiteThemeResolver>.Instance));
        context.Services.AddSingleton(ordering);
        context.Services.AddSingleton(new StoreNavigation(siteContext, ordering));

        return context.RenderComponent<SiteHeader>();
    }
}
