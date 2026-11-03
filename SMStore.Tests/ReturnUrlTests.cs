using FluentAssertions;
using SMStore.Accounts;
using Xunit;

namespace SMStore.Tests;

// Sign-in and every basket button redirect to a posted returnUrl. Anything that is not a path
// on this host makes them an open redirect from the store's real domain.
public class ReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/catalog?category=printers&page=2")]
    [InlineData("/account/quotes/QT-0041")]
    public void APathOnThisHostIsHonoured(string url) =>
        CustomerAuthEndpoints.IsLocalPath(url).Should().BeTrue();

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    // Browsers strip tabs and newlines from a URL, which turns each of these into the first.
    [InlineData("/\t/evil.example")]
    [InlineData("/\n/evil.example")]
    [InlineData("/\r/evil.example")]
    [InlineData("https://evil.example/")]
    [InlineData("evil.example")]
    [InlineData("~/catalog")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsNot(string? url) =>
        CustomerAuthEndpoints.IsLocalPath(url).Should().BeFalse();
}
