using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.Email
{
    /// <summary>A message ready for the transport.</summary>
    /// <param name="WordingRefused">
    /// Why the store's own wording was not used, when it existed and failed its checks. The
    /// message still goes, in the platform's wording; the caller logs this so somebody fixes
    /// the row.
    /// </param>
    public sealed record RenderedEmail(string Subject, string Body, string WordingRefused = null);

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
    /// that cannot happen, and <c>EmailTemplateTests</c> is what says so; in a store's wording
    /// it is refused before rendering. A blank would hide the mistake inside a message that
    /// reads as if it were finished.
    /// </remarks>
    public static class EmailRenderer
    {
        private static readonly Regex Placeholder = new(
            @"\{(?<name>[A-Za-z][A-Za-z0-9]*)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Fragments such as a reference line render empty when there is nothing to say, and
        // leave the blank lines either side of them behind.
        private static readonly Regex BlankRun = new(@"\n{3,}", RegexOptions.Compiled);

        /// <param name="siteWording">
        /// The store's own wording, from <c>dbo.SiteEmailTemplate</c>, or null for the platform's.
        /// </param>
        public static RenderedEmail Render(
            EmailTemplate template, SiteModel site, string payloadJson,
            SiteEmailTemplateModel siteWording = null)
        {
            var values = ValuesFor(template, site, payloadJson);
            var refused = siteWording is null ? null : WhyRefused(template, site, siteWording);
            var useWording = siteWording is not null && refused is null;

            var subject = useWording && !string.IsNullOrWhiteSpace(siteWording.Subject)
                ? siteWording.Subject
                : template.DefaultSubject;

            var body = useWording && !string.IsNullOrWhiteSpace(siteWording.Body)
                ? siteWording.Body
                : template.DefaultBody;

            return new RenderedEmail(Fill(subject, values).Trim(), Tidy(Fill(body, values)), refused);
        }

        /// <summary>
        /// Why a store's wording cannot be used for this message, or null when it can.
        /// </summary>
        /// <remarks>
        /// Two checks, both of which would otherwise surface as a customer's mail. A
        /// placeholder the message has no value for would arrive as <c>{Compnay}</c>; a
        /// required one left out would send a reset mail with no link in it. Refusing the
        /// whole row rather than half of it keeps a subject and a body that were written to go
        /// together from being mixed with the platform's.
        /// </remarks>
        public static string WhyRefused(EmailTemplate template, SiteModel site, SiteEmailTemplateModel siteWording)
        {
            var known = ValuesFor(template, site, null).Keys.ToHashSet();

            var unknown = PlaceholdersIn(siteWording.Subject)
                .Concat(PlaceholdersIn(siteWording.Body))
                .Where(name => !known.Contains(name))
                .Distinct()
                .ToList();

            if (unknown.Count > 0)
            {
                return $"it uses {string.Join(", ", unknown.Select(name => $"{{{name}}}"))}, " +
                       $"which {template.Key} has no value for";
            }

            if (string.IsNullOrWhiteSpace(siteWording.Body))
            {
                return null;
            }

            var missing = template.RequiredTokens
                .Except(PlaceholdersIn(siteWording.Body))
                .ToList();

            return missing.Count > 0
                ? $"it leaves out {string.Join(", ", missing.Select(name => $"{{{name}}}"))}, " +
                  $"which {template.Key} cannot do without"
                : null;
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
