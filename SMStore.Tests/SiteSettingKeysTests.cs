using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SMDataManager.Library.Models;
using SMDataManager.Library.Tax;
using SMStore.Ordering;
using SMStore.Registration;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// SiteSettingKeys is what the admin settings screen offers and the API accepts (T9). The
/// implementations behind the keys are registered here and in the library, which the API cannot
/// see, so the list is a second statement of the same fact — and these are what keep the two
/// from disagreeing. A key listed with nothing behind it lets an admin save a store into a 500;
/// an implementation with no key is one no admin can choose.
/// </summary>
public class SiteSettingKeysTests
{
    private static IReadOnlyList<string> Keys<T>(Func<T, string> key, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);

        using var provider = services.BuildServiceProvider();

        return provider.GetServices<T>().Select(key).ToList();
    }

    [Fact]
    public void EveryOrderingModeKeyHasAnImplementationAndTheReverse()
    {
        Keys<IOrderingMode>(mode => mode.Key, services => services.AddOrderingModes())
            .Should().BeEquivalentTo(SiteSettingKeys.OrderModes);
    }

    [Fact]
    public void EveryRegistrationFieldSetKeyHasAnImplementationAndTheReverse()
    {
        Keys<IRegistrationFieldSet>(set => set.Key, services => services.AddRegistrationFieldSets())
            .Should().BeEquivalentTo(SiteSettingKeys.RegistrationFieldSets);
    }

    [Fact]
    public void EveryTaxRuleSetKeyHasAnImplementationAndTheReverse()
    {
        Keys<ITaxRuleSet>(set => set.Key, services => services.AddTaxRuleSets())
            .Should().BeEquivalentTo(SiteSettingKeys.TaxRuleSets);
    }

    [Fact]
    public void EveryContentPageRouteIsAContentKeyAndTheReverse()
    {
        // The admin content screen offers SiteContentKeys; ContentPage declares its routes as
        // @page lines. A key with no route is a page an admin writes and nobody can open; a route
        // with no key is one the store can never fill. The home page is its own component.
        var routes = typeof(SMStore.Components.Pages.ContentPage)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Components.RouteAttribute>()
            .Select(route => route.Template.Trim('/'));

        routes.Should().BeEquivalentTo(SiteContentKeys.All
            .Select(entry => entry.Key)
            .Where(key => key != SiteContentKeys.Home));
    }
}
