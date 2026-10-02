using System.Reflection;
using FluentAssertions;
using SMStore.Accounts;
using StockManager.Notifications;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// A guard rather than a behaviour check: nothing in the storefront may hold the mail
/// transport at all.
/// </summary>
/// <remarks>
/// The storefront queues and <c>StockApi</c> sends. A page that injected
/// <see cref="IEmailSender"/> again would send from inside a request, unretried and
/// unreported, and the change would read as a simplification. <c>SMStore/Program.cs</c> no
/// longer registers a transport, so it would also fail to resolve at run time — this says so
/// at build time instead, and covers an <c>[Inject]</c> property and a minimal-API parameter
/// as well as a constructor.
/// </remarks>
public class TransportCallerTests
{
    [Fact]
    public void NoPageEndpointOrServiceHoldsTheTransport()
    {
        const BindingFlags everything = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var holders = new List<string>();

        foreach (var type in typeof(PasswordResetService).Assembly.GetTypes())
        {
            holders.AddRange(type.GetMethods(everything).Cast<MethodBase>()
                .Concat(type.GetConstructors(everything))
                .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(IEmailSender)))
                .Select(method => $"{type.FullName}.{method.Name}"));

            holders.AddRange(type.GetFields(everything)
                .Where(field => field.FieldType == typeof(IEmailSender))
                .Select(field => $"{type.FullName}.{field.Name}"));

            holders.AddRange(type.GetProperties(everything)
                .Where(property => property.PropertyType == typeof(IEmailSender))
                .Select(property => $"{type.FullName}.{property.Name}"));
        }

        holders.Should().BeEmpty();
    }
}
