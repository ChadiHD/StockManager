using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockApi.Controllers;
using StockApi.Security;
using Xunit;

namespace StockApi.Tests.Controllers;

// Guards rather than behaviour checks. Staff and customers share one Identity store, and until
// T7 /token issued a customer a (roleless) token for their storefront password. It no longer
// does, but a roleless token is still one class-level [Authorize] away from opening something:
// nothing would fail, the action would simply answer. So every action must name the roles it
// admits, and the exceptions are listed here where adding one is a visible decision.
public class AuthorizationSurfaceTests
{
    private const BindingFlags ActionFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly HashSet<string> Allowed =
    [
        // The sign-in itself.
        $"{nameof(TokenController)}.{nameof(TokenController.Create)}",
        // A signed-in caller's own profile, which is how the portal and the POS find their name.
        $"{nameof(UserController)}.{nameof(UserController.GetById)}",
    ];

    private static IEnumerable<(Type Controller, MethodInfo Action)> All() =>
        typeof(TokenController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(ActionFlags)
                .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null)
                .Select(method => (type, method)));

    private static string Name((Type Controller, MethodInfo Action) action) =>
        $"{action.Controller.Name}.{action.Action.Name}";

    public static IEnumerable<object[]> Actions() =>
        All().Select(Name).Distinct().Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Actions))]
    public void EveryActionNamesTheRolesItAdmits(string action)
    {
        if (Allowed.Contains(action))
        {
            return;
        }

        // Every overload, since each is its own endpoint.
        foreach (var (controller, method) in All().Where(candidate => Name(candidate) == action))
        {
            method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull($"{action} is open to anyone");

            var roles = method.GetCustomAttributes<AuthorizeAttribute>()
                .Concat(controller.GetCustomAttributes<AuthorizeAttribute>())
                .Select(attribute => attribute.Roles)
                .Where(value => !string.IsNullOrWhiteSpace(value));

            roles.Should().NotBeEmpty($"{action} admits any signed-in caller, including one with no role");
        }
    }

    [Fact]
    public void TheAllowListOnlyNamesActionsThatExist()
    {
        Allowed.Except(All().Select(Name)).Should().BeEmpty();
    }

    // Actions that are not about one store, which an admin given only some stores must not
    // reach (T9): staff and their grants, the shared product table, and the till. The thing
    // that matters is again an absence — remove the policy and every one of these still works,
    // for everybody — so it is listed here rather than left to review.
    private static readonly HashSet<string> EveryStoreOnly =
    [
        $"{nameof(UserController)}.{nameof(UserController.Register)}",
        $"{nameof(UserController)}.{nameof(UserController.GetAllUsers)}",
        $"{nameof(UserController)}.{nameof(UserController.GetAllStaff)}",
        $"{nameof(UserController)}.{nameof(UserController.SetStores)}",
        $"{nameof(UserController)}.{nameof(UserController.GetAllRoles)}",
        $"{nameof(UserController)}.{nameof(UserController.AddRole)}",
        $"{nameof(UserController)}.{nameof(UserController.RemoveRole)}",
        $"{nameof(ProductController)}.{nameof(ProductController.GetBySku)}",
        $"{nameof(ProductController)}.{nameof(ProductController.Create)}",
        $"{nameof(ProductController)}.{nameof(ProductController.Update)}",
        $"{nameof(ProductController)}.{nameof(ProductController.EnrichImages)}",
        $"{nameof(PurchaseController)}.{nameof(PurchaseController.GetPurchaseReports)}",
        $"{nameof(InventoryController)}.{nameof(InventoryController.Get)}",
        $"{nameof(InventoryController)}.{nameof(InventoryController.Post)}",
    ];

    public static IEnumerable<object[]> EveryStoreActions() =>
        EveryStoreOnly.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(EveryStoreActions))]
    public void ActionsNotAboutOneStoreTakeAnAdminOfEveryStore(string action)
    {
        var overloads = All().Where(candidate => Name(candidate) == action).ToList();

        overloads.Should().NotBeEmpty($"{action} is listed but does not exist");

        foreach (var (controller, method) in overloads)
        {
            method.GetCustomAttributes<AuthorizeAttribute>()
                .Concat(controller.GetCustomAttributes<AuthorizeAttribute>())
                .Select(attribute => attribute.Policy)
                .Should().Contain(AllStores.Policy, $"{action} is open to an admin given only some stores");
        }
    }

    [Theory]
    [InlineData("admin@example.test", true)]
    [InlineData("aclitrade|buyer@example.test", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ACustomerLoginIsNeverAStaffName(string? userName, bool isStaff)
    {
        StaffSignIn.IsStaffName(userName).Should().Be(isStaff);
    }
}
