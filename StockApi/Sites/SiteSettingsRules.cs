using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using SMDataManager.Library.Models;

namespace StockApi.Sites;

/// <summary>
/// What a store's settings must look like before they are saved (T9). The checks the database
/// cannot make — which keys code implements, what a domain or a locale is — so that a store
/// saved from the screen cannot fail on its next request the way a hand-edited row could.
/// </summary>
public static partial class SiteSettingsRules
{
    /// <summary>Why <paramref name="site"/> cannot be saved, or null. Normalises case as it goes.</summary>
    public static string? WhyRefused(SiteModel site)
    {
        site.Name = site.Name?.Trim() ?? string.Empty;
        site.Domain = site.Domain?.Trim().ToLowerInvariant() ?? string.Empty;
        site.Country = site.Country?.Trim().ToUpperInvariant() ?? string.Empty;
        site.CurrencyCode = site.CurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        site.Locale = site.Locale?.Trim() ?? string.Empty;

        if (site.Name.Length is 0 or > 200) return "A store needs a name of up to 200 characters.";

        // A bare host: what SiteResolutionMiddleware compares the request's Host against. A
        // scheme, a path or a port would make it match nothing, and the store would 404.
        if (site.Domain.Length > 253 || !HostName().IsMatch(site.Domain))
            return "The domain is a host name only, such as shop.example.com — no https://, path or port.";

        if (!TwoLetters().IsMatch(site.Country)) return "The country is a two-letter code, such as IE or GB.";

        if (!ThreeLetters().IsMatch(site.CurrencyCode)) return "The currency is a three-letter code, such as EUR or GBP.";

        if (!IsKnownCulture(site.Locale)) return $"'{site.Locale}' is not a locale this server knows, such as en-IE or en-GB.";

        site.OrderMode = Canonical(SiteSettingKeys.OrderModes, site.OrderMode)!;
        if (site.OrderMode is null) return "That ordering mode does not exist.";

        site.RegistrationFieldSet = Canonical(SiteSettingKeys.RegistrationFieldSets, site.RegistrationFieldSet)!;
        if (site.RegistrationFieldSet is null) return "That registration form does not exist.";

        site.TaxRuleSet = Canonical(SiteSettingKeys.TaxRuleSets, site.TaxRuleSet)!;
        if (site.TaxRuleSet is null) return "Those tax rules do not exist.";

        site.PriceDisplay = Canonical(SiteSettingKeys.PriceDisplays, site.PriceDisplay)!;
        if (site.PriceDisplay is null) return "Prices are shown to everyone or to signed-in customers only.";

        foreach (var (label, address) in new[] { ("operator", site.OperatorEmail), ("sending", site.MailFromAddress) })
        {
            if (!string.IsNullOrWhiteSpace(address) && !MailAddress.TryCreate(address.Trim(), out _))
                return $"The {label} address is not an email address.";
        }

        if ((site.TaxRegistrationNumber?.Length ?? 0) > 30) return "The VAT number is at most 30 characters.";
        if ((site.LegalName?.Length ?? 0) > 200) return "The legal name is at most 200 characters.";
        if ((site.CompanyRegistrationNumber?.Length ?? 0) > 50) return "The company number is at most 50 characters.";
        if ((site.RegisteredAddress?.Length ?? 0) > 400) return "The registered office is at most 400 characters.";

        return null;
    }

    // Stored in the list's own spelling, so the providers' case-insensitive match and an
    // ordinal comparison anywhere else — CatalogPresenter's, for one — agree on what was saved.
    private static string? Canonical(IReadOnlyList<string> keys, string? value) =>
        keys.FirstOrDefault(key => string.Equals(key, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsKnownCulture(string name)
    {
        if (name.Length == 0) return false;

        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true).Name.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$")]
    private static partial Regex HostName();

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex TwoLetters();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex ThreeLetters();
}
