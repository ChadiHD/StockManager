using FluentAssertions;
using StockApi.Sites;
using Xunit;

namespace StockApi.Tests.Sites;

/// <summary>
/// AdminSiteContext exists to fail loudly rather than default — see the remarks on
/// IAdminSiteContext. A controller that reads Site before the middleware has resolved one must
/// get an exception, not a null site silently serving whichever store happens to come first.
/// </summary>
/// <remarks>
/// Resolve() is internal, called only by AdminSiteResolutionMiddleware in the same assembly, so
/// the resolved-and-readable side of this contract is exercised there instead of here — see
/// AdminSiteResolutionMiddlewareTests.
/// </remarks>
public class AdminSiteContextTests
{
    [Fact]
    public void SiteThrowsBeforeAnyStoreHasBeenResolved()
    {
        var context = new AdminSiteContext();

        var act = () => context.Site;

        act.Should().Throw<SiteNotResolvedException>();
    }

    [Fact]
    public void SiteIdThrowsBeforeAnyStoreHasBeenResolvedToo()
    {
        // Documented as shorthand for Site with the same failure behaviour — a shortcut that
        // swallowed the exception would be the more dangerous one to get wrong, since every
        // scoped query calls this rather than Site itself.
        var context = new AdminSiteContext();

        var act = () => context.SiteId;

        act.Should().Throw<SiteNotResolvedException>();
    }

    [Fact]
    public void IsResolvedIsFalseBeforeAnyStoreHasBeenResolved()
    {
        var context = new AdminSiteContext();

        context.IsResolved.Should().BeFalse();
    }
}
