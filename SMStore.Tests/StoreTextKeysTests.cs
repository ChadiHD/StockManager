using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// The storefront's words against its translations (T10). Every <c>L["…"]</c> in SMStore is a
/// key, and a translation that lacks one renders that phrase in English on a store that is not.
/// Nothing else would notice: the page works, every test passes, and the Portuguese customer
/// reads an English button.
/// </summary>
/// <remarks>
/// The template ships English only, so until the first <c>StoreText.{culture}.resx</c> arrives
/// the translation half passes with nothing to check. That is the point of having it now: the
/// first translation is held to the whole storefront the day it is added.
/// </remarks>
public partial class StoreTextKeysTests
{
    private static readonly string StoreDirectory = FindStore();

    [Fact]
    public void TheStorefrontsWordsGoThroughTheLocaliser()
    {
        // A floor, so a regex that stopped matching cannot make the translation check below pass
        // by finding no keys at all.
        Keys().Should().HaveCountGreaterThan(200);
    }

    [Fact]
    public void NoKeyIsBuiltAtRunTime()
    {
        // L[$"…"] and L[x + "…"] are keys no translation file can contain. A variable goes in as a
        // placeholder: L["Valid until {0}", date].
        var built = SourceFiles()
            .SelectMany(file => DynamicKey().Matches(File.ReadAllText(file))
                .Select(match => $"{Path.GetRelativePath(StoreDirectory, file)}: {match.Value}"))
            .ToList();

        built.Should().BeEmpty();
    }

    [Fact]
    public void EveryTranslationHasEveryKeyAndNoOthers()
    {
        var keys = Keys();

        foreach (var translation in Directory.EnumerateFiles(
                     Path.Combine(StoreDirectory, "Localization"), "StoreText.*.resx"))
        {
            var translated = XDocument.Load(translation).Root!.Elements("data")
                .Select(data => (string)data.Attribute("name")!)
                .ToHashSet(StringComparer.Ordinal);

            keys.Except(translated).Should().BeEmpty($"{Path.GetFileName(translation)} renders these in English");
            translated.Except(keys).Should().BeEmpty($"{Path.GetFileName(translation)} translates words nothing shows");
        }
    }

    private static HashSet<string> Keys() =>
        SourceFiles()
            .SelectMany(file => LiteralKey().Matches(File.ReadAllText(file)))
            .Select(match => Regex.Unescape(match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(StoreDirectory, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            // Its documentation shows the call, and its examples are not words a page shows.
            .Where(file => Path.GetFileName(file) != "StoreText.cs");

    // From this file's own path at compile time, not from the test assembly's, because a build
    // with --artifacts-path puts the assembly nowhere near the source.
    private static string FindStore([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "SMStore"));

    [GeneratedRegex(@"\bL\[""((?:[^""\\]|\\.)*)""")]
    private static partial Regex LiteralKey();

    [GeneratedRegex(@"\bL\[\s*(\$|@\$|\$@|[A-Za-z_][\w.]*\s*[+\]])")]
    private static partial Regex DynamicKey();
}
