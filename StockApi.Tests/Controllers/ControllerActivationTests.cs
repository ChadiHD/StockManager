using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using StockApi.Controllers;
using StockApi.Tests.TestSupport;
using Xunit;

namespace StockApi.Tests.Controllers;

// MVC does not register controllers as services, so a constructor asking for a type the host
// never registered compiles, starts, passes ValidateOnBuild and fails only when its route is
// hit. InventoryController asked for the concrete InventoryData beside an IInventoryData
// registration, and api/Inventory answered 500 before either action ran. The controller tests
// construct controllers by hand and cannot see that, so this reads Program's own container.
public class ControllerActivationTests
{
    [Fact]
    public void EveryControllerConstructorAsksOnlyForRegisteredServices()
    {
        using var host = ProgramHost.Build();
        var isService = host.Services.GetRequiredService<IServiceProviderIsService>();

        var feature = new ControllerFeature();
        host.Services.GetRequiredService<ApplicationPartManager>().PopulateFeature(feature);

        // Discovery depends on the application name; without this a wrong one passes vacuously.
        feature.Controllers.Should().Contain(typeof(InventoryController).GetTypeInfo());

        feature.Controllers
            .SelectMany(controller => controller.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => !parameter.HasDefaultValue && !isService.IsService(parameter.ParameterType))
                .Select(parameter => $"{controller.Name}({parameter.ParameterType.Name} {parameter.Name})"))
            .Should().BeEmpty("MVC cannot activate a controller whose constructor names an unregistered type");
    }
}
