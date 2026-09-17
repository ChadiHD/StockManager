using System.Collections.Frozen;
using System.Globalization;

namespace SMStore.Registration;

/// <summary>
/// The country list the registration form offers.
/// </summary>
/// <remarks>
/// Derived from the runtime's own region data rather than typed out, because a hand-written
/// list is a list that goes stale and a list somebody trims to "the countries we sell to" —
/// which is a commercial decision that belongs to a field set, not to a dropdown.
///
/// Codes are ISO 3166-1 alpha-2, matching <c>dbo.Address.Country</c> and
/// <c>dbo.Site.Country</c>, so an address can be compared with the store's own country when
/// T6 works out the tax treatment. That comparison is the reason this is a select and not a
/// text box: "Ireland", "IRL" and "ie" are all the same country and none of them is
/// comparable with "IE".
/// </remarks>
public static class RegistrationCountries
{
    /// <summary>Code to English name, ordered by name.</summary>
    public static readonly FrozenDictionary<string, string> All = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(culture =>
        {
            try
            {
                return new RegionInfo(culture.Name);
            }
            catch (ArgumentException)
            {
                // A culture with no region — "sr-Cyrl" and friends. Skipped rather than
                // guessed at.
                return null;
            }
        })
        .Where(region => region is not null && region.TwoLetterISORegionName.Length == 2)
        .GroupBy(region => region!.TwoLetterISORegionName, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First()!)
        .OrderBy(region => region.EnglishName, StringComparer.OrdinalIgnoreCase)
        .ToFrozenDictionary(
            region => region.TwoLetterISORegionName.ToUpperInvariant(),
            region => region.EnglishName);

    public static string Name(string? code) =>
        code is not null && All.TryGetValue(code.Trim().ToUpperInvariant(), out var name) ? name : code ?? string.Empty;
}
