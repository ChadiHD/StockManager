using System.Collections.Generic;
using System.Text.RegularExpressions;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.Email
{
    /// <summary>A message ready for the transport.</summary>
    public sealed record RenderedEmail(string Subject, string Body);

    /// <summary>
    /// Fills a template's placeholders from its payload and the store it is sent from.
    /// </summary>
    /// <remarks>
    /// One pass, left to right, and a substituted value is never scanned again. That is the
    /// whole of the escaping rule for plain text, and it matters: a rejection reason that
    /// happens to contain <c>{ConfirmationParagraph}</c> must come out as those characters,
    /// not as somebody else's link. <c>Regex.Replace</c> gives that for free — the replacement
    /// is output, not input.
    ///
    /// A placeholder with no value is left as written rather than blanked. In platform wording
    /// that cannot happen, and <c>EmailTemplateTests</c> is what says so; a blank would hide the
    /// mistake inside a message that reads as if it were finished.
    /// </remarks>
    public static class EmailRenderer
    {
        private static readonly Regex Placeholder = new(
            @"\{(?<name>[A-Za-z][A-Za-z0-9]*)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Fragments such as a reference line render empty when there is nothing to say, and
        // leave the blank lines either side of them behind.
        private static readonly Regex BlankRun = new(@"\n{3,}", RegexOptions.Compiled);

        public static RenderedEmail Render(EmailTemplate template, SiteModel site, string payloadJson)
        {
            var values = ValuesFor(template, site, payloadJson);

            return new RenderedEmail(
                Fill(template.DefaultSubject, values).Trim(),
                Tidy(Fill(template.DefaultBody, values)));
        }

        /// <summary>The payload's values plus the store's own, which every template may use.</summary>
        public static IReadOnlyDictionary<string, string> ValuesFor(
            EmailTemplate template, SiteModel site, string payloadJson)
        {
            var values = new Dictionary<string, string>(template.Values(site, payloadJson))
            {
                ["SiteName"] = site.Name ?? string.Empty,
                ["SiteDomain"] = site.Domain ?? string.Empty,
            };

            return values;
        }

        /// <summary>The placeholders a piece of wording uses, for checking it before it is used.</summary>
        public static IEnumerable<string> PlaceholdersIn(string text)
        {
            foreach (Match match in Placeholder.Matches(text ?? string.Empty))
            {
                yield return match.Groups["name"].Value;
            }
        }

        private static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
            Placeholder.Replace(text ?? string.Empty, match =>
                values.TryGetValue(match.Groups["name"].Value, out var value)
                    ? value ?? string.Empty
                    : match.Value);

        private static string Tidy(string body) =>
            BlankRun.Replace(body.Replace("\r\n", "\n"), "\n\n").Trim() + "\n";
    }
}
