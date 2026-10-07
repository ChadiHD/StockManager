using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockApi.Controllers;
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
        using var host = BuildProgramHost();
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

    // Runs Program up to builder.Build() and aborts it there, the way dotnet-ef and
    // WebApplicationFactory reach a minimal-hosting app's container: everything after Build
    // migrates a database and seeds an admin, and this needs neither. Development, because that
    // is where ValidateOnBuild checks every registration's own dependencies, which leaves the
    // controllers — not registrations — as the one gap this test covers.
    private static IHost BuildProgramHost()
    {
        IHost? host = null;
        var thread = Environment.CurrentManagedThreadId;

        // Other test classes build hosts in parallel; only the one built on this thread is ours.
        using var subscription = DiagnosticListener.AllListeners.Subscribe(new Observer<DiagnosticListener>(listener =>
        {
            if (listener.Name != "Microsoft.Extensions.Hosting")
            {
                return;
            }

            listener.Subscribe(new Observer<KeyValuePair<string, object?>>(diagnostic =>
            {
                if (diagnostic.Key == "HostBuilt" && Environment.CurrentManagedThreadId == thread)
                {
                    host = (IHost)diagnostic.Value!;
                    throw new HostAbortedException();
                }
            }));
        }));

        string[] args =
        [
            "--environment=Development",
            // The test runner is the entry assembly, and controller discovery starts from this name.
            "--applicationName=StockApi",
            "--Jwt:SigningKey=controller-activation-test-signing-key-0123456789",
        ];

        try
        {
            var result = typeof(InventoryController).Assembly.EntryPoint!.Invoke(null, [args]);
            (result as Task)?.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HostAbortedException || exception.InnerException is HostAbortedException)
        {
        }

        return host ?? throw new InvalidOperationException("Program returned without building a host.");
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
