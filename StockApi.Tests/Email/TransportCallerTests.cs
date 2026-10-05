using System.Reflection;
using FluentAssertions;
using StockApi.Email;
using StockManager.Notifications;
using Xunit;

namespace StockApi.Tests.Email;

/// <summary>
/// A guard rather than a behaviour check: <see cref="EmailDispatcher"/> is the only thing in
/// this host that may hold the transport.
/// </summary>
/// <remarks>
/// A controller that took <see cref="IEmailSender"/> again would compile, pass every test of
/// its own, and send mail that is never retried, never dead-lettered and never reported —
/// from inside a request, waiting on a relay. That is the shape T6 removed, and the
/// "simplification" that would bring it back reads as an improvement. So this looks for the
/// type anywhere it could be injected: a constructor, a method parameter, a field, a property.
/// <c>SMStore.Tests</c> holds the storefront to the stricter rule that nothing there may hold it.
/// </remarks>
public class TransportCallerTests
{
    [Fact]
    public void NothingButTheDispatcherCanReachTheTransport()
    {
        TransportHolders(typeof(EmailDispatcher).Assembly)
            .Where(holder => !IsPartOf(holder.Type, typeof(EmailDispatcher)))
            .Select(holder => holder.Where)
            .Should().BeEmpty();
    }

    internal static IEnumerable<(Type Type, string Where)> TransportHolders(Assembly assembly)
    {
        const BindingFlags everything = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(everything).Cast<MethodBase>().Concat(type.GetConstructors(everything)))
            {
                if (method.GetParameters().Any(parameter => parameter.ParameterType == typeof(IEmailSender)))
                {
                    yield return (type, $"{type.FullName}.{method.Name}");
                }
            }

            foreach (var field in type.GetFields(everything).Where(field => field.FieldType == typeof(IEmailSender)))
            {
                yield return (type, $"{type.FullName}.{field.Name}");
            }

            foreach (var property in type.GetProperties(everything).Where(property => property.PropertyType == typeof(IEmailSender)))
            {
                yield return (type, $"{type.FullName}.{property.Name}");
            }
        }
    }

    // Its async state machines and closures are compiler-generated types nested inside it.
    private static bool IsPartOf(Type type, Type owner)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current == owner) return true;
        }

        return false;
    }
}
