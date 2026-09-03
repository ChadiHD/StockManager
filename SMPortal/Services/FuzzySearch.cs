using FuzzySharp;

namespace SMPortal.Services;

// Fuzzy, score-ranked search over a list already held in the client-side snapshot. Nothing
// leaves the browser, so this stays responsive as the operator types.
public static class FuzzySearch
{
    /// <summary>Minimum score a row must reach to be considered a match.</summary>
    public const int DefaultThreshold = 60;

    /// <summary>
    /// Ranks <paramref name="source"/> against <paramref name="term"/>, best match first.
    /// <paramref name="text"/> fields are scored with WeightedRatio, tolerant of word order and
    /// misspellings. <paramref name="codes"/> fields — SKUs, MPNs, barcodes, emails — match on
    /// substring only: they are identifiers, and near-misses between them are different things,
    /// not typos. Fuzzy scoring there put every sibling SKU above the threshold, so searching
    /// one SKU returned the whole catalogue.
    /// </summary>
    public static IEnumerable<T> Rank<T>(
        IEnumerable<T> source,
        string? term,
        Func<T, string?[]> text,
        Func<T, string?[]>? codes = null,
        int threshold = DefaultThreshold)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return source;
        }

        var needle = term.Trim();
        var tokens = needle.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries);

        // Two passes, because WeightedRatio is orders of magnitude dearer than Contains and
        // almost every real search is spelled correctly. The literal pass alone answers those,
        // and only a term that matches nothing at all is worth paying the fuzzy price for.
        var literal = Collect(source, needle, tokens, text, codes, threshold, fuzzy: false);

        // A multi-word term needs every word to match, so a fuzzy rescue would have to correct
        // all of them at once. It effectively never fires, yet it is the dearest case to run —
        // so the fuzzy pass is reserved for a single misspelled word, which is what it fixes.
        return literal.Count > 0 || tokens.Length > 1
            ? literal
            : Collect(source, needle, tokens, text, codes, threshold, fuzzy: true);
    }

    private static List<T> Collect<T>(
        IEnumerable<T> source,
        string needle,
        string[] tokens,
        Func<T, string?[]> text,
        Func<T, string?[]>? codes,
        int threshold,
        bool fuzzy)
    {
        return source
            .Select(item => new
            {
                Item = item,
                Score = Score(needle, tokens, text(item), codes?.Invoke(item), threshold, fuzzy)
            })
            .Where(result => result.Score >= threshold)
            .OrderByDescending(result => result.Score)
            .Select(result => result.Item)
            .ToList();
    }

    private static int Score(
        string term, string[] tokens, string?[] textFields, string?[]? codeFields,
        int threshold, bool fuzzy)
    {
        // The whole phrase first: an exact or near-exact hit on a single field should always
        // win, and for a one-word term this is the only pass that runs.
        var phrase = ScoreTerm(term, textFields, codeFields, fuzzy);

        if (tokens.Length < 2) return phrase;

        // Every word must be found somewhere, but not necessarily in the same field — that is
        // what lets "lenovo thinkpad" match one row whose description carries both, or a
        // manufacturer of Lenovo beside a name of ThinkPad. The weakest word bounds the row,
        // so one unmatched word rules the row out entirely.
        var weakest = 100;
        foreach (var token in tokens)
        {
            weakest = Math.Min(weakest, ScoreTerm(token, textFields, codeFields, fuzzy));
            if (weakest < threshold) break;
        }

        return Math.Max(phrase, weakest);
    }

    private static int ScoreTerm(
        string term, string?[] textFields, string?[]? codeFields, bool fuzzy)
    {
        var best = 0;

        foreach (var value in Concat(textFields, codeFields))
        {
            if (string.IsNullOrEmpty(value)) continue;

            if (value.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                // An exact whole-value match should outrank a match buried in a longer string.
                var exact = value.Length == term.Length;
                best = Math.Max(best, exact ? 100 : 95);
            }
        }

        if (best > 0 || !fuzzy) return best;

        // Only the prose fields fall through to fuzzy scoring. Code fields had their chance
        // above and deliberately get no second one.
        foreach (var value in textFields)
        {
            if (!string.IsNullOrEmpty(value)) best = Math.Max(best, Fuzz.WeightedRatio(term, value));
        }

        return best;
    }

    private static IEnumerable<string?> Concat(string?[] first, string?[]? second)
    {
        foreach (var value in first) yield return value;

        if (second is null) yield break;
        foreach (var value in second) yield return value;
    }
}
