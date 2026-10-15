using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace SMStore.Localization;

/// <summary>
/// The storefront's words, in the store's language (T10).
/// </summary>
/// <remarks>
/// <c>L["Add to quote"]</c>, in markup through <c>@using static</c> in <c>_Imports.razor</c> and in
/// C# through the same using. **The English text is the key.** A translation is a
/// <c>StoreText.{culture}.resx</c> beside this file, and a key it does not have renders as the
/// English, which is the framework's own fallback. So an English store needs no file at all and a
/// page's markup still reads as its words.
///
/// The culture is the store's: <c>SiteResolutionMiddleware</c> sets <c>CurrentUICulture</c> from
/// <c>Site.Locale</c>, and the localiser reads it at each lookup.
///
/// **A phrase is one key.** Never lowercase a localised word, append an "s" to it, or join two
/// keys into a sentence, because word order, plurals, capitals and gender are per language.
/// Anything variable goes in as a placeholder: <c>L["Valid until {0}", date]</c>.
///
/// Static rather than injected, deliberately. A lookup over embedded resources has no
/// per-request state and nothing to substitute, and injecting it would have added a service
/// every component and every bUnit test context had to register, for no behaviour.
/// <c>StoreTextKeysTests</c> reads every <c>L["…"]</c> in the storefront and holds each
/// translation to the whole set.
/// </remarks>
public static class StoreText
{
    public static readonly IStringLocalizer L =
        new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)
            .Create(typeof(StoreText));
}
