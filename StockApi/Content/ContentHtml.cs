using AngleSharp.Css.Dom;
using Ganss.Xss;

namespace StockApi.Content;

/// <summary>
/// Cleans a content page's body before it is stored (T9). The storefront renders
/// <c>SiteContent.BodyHtml</c> as markup, and until the content screen existed only someone with
/// database access could write it. Now any admin of a store can, including one given only that
/// store, so what they type is held to an allow-list.
/// </summary>
/// <remarks>
/// The storefront's CSP already refuses inline script. What it does not refuse is a form that
/// posts somewhere, an iframe, a style that dresses a link up as the sign-in button, or a
/// <c>javascript:</c> URL, so the list is structure and links only: no forms, no frames, no
/// <c>style</c> or <c>class</c>, no event handlers, and only https, mailto and tel links.
/// Relative links are kept, because "see our <a href="/terms">terms</a>" is the common case.
///
/// HtmlSanitizer, not a hand-written filter, because a hand-written filter is the classic bug.
/// It lives here rather than in SMDataManager.Library so the storefront can render without it:
/// it needs an AngleSharp newer than bUnit 1.40 can run with (CLAUDE.md, "Do not add an explicit
/// AngleSharp PackageReference"). So the guarantee is at the one write path the portal has.
/// Rows written by SQL are trusted, as every row was before T9.
/// </remarks>
public static class ContentHtml
{
    private static readonly HtmlSanitizer Sanitizer = new(new HtmlSanitizerOptions
    {
        AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "h2", "h3", "h4", "p", "br", "hr", "ul", "ol", "li", "strong", "em", "b", "i",
            "blockquote", "a", "img", "table", "thead", "tbody", "tr", "th", "td"
        },
        AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "href", "src", "alt", "title", "colspan", "rowspan"
        },
        UriAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "src" },
        AllowedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "https", "mailto", "tel" },
        AllowedCssProperties = new HashSet<string>(),
        AllowedAtRules = new HashSet<CssRuleType>(),
        AllowedCssClasses = new HashSet<string>(),
    });

    public static string? Sanitize(string? html) =>
        string.IsNullOrWhiteSpace(html) ? html : Sanitizer.Sanitize(html);
}
