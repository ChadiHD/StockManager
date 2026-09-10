using SMStore.Sites;

namespace SMStore.Ordering;

/// <summary>
/// Picks the ordering behaviour for the current store. Injected wherever a component would
/// otherwise ask "are we a quote site or a checkout site?" — that question is answered here
/// once, not at each call site.
/// </summary>
public sealed class OrderingModeProvider
{
    private readonly ISiteContext _siteContext;
    private readonly IReadOnlyDictionary<string, IOrderingMode> _modes;

    public OrderingModeProvider(ISiteContext siteContext, IEnumerable<IOrderingMode> modes)
    {
        _siteContext = siteContext;
        _modes = modes.ToDictionary(mode => mode.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IOrderingMode Current
    {
        get
        {
            var key = _siteContext.Site.OrderMode;

            // Failing loudly beats defaulting: a site configured for a mode that is not built
            // yet would otherwise quietly render the wrong buttons and take the wrong actions.
            return _modes.TryGetValue(key, out var mode)
                ? mode
                : throw new InvalidOperationException(
                    $"Site '{_siteContext.Site.SiteKey}' is configured with OrderMode '{key}', " +
                    $"for which no IOrderingMode is registered. Known modes: " +
                    $"{string.Join(", ", _modes.Keys)}.");
        }
    }
}
