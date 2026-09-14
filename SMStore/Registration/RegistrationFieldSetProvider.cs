using SMStore.Sites;

namespace SMStore.Registration;

/// <summary>
/// Picks the registration field set for the current store. Injected wherever a component
/// would otherwise ask "what does this store demand of an applicant?" — that question is
/// answered here once, not at each call site.
/// </summary>
public sealed class RegistrationFieldSetProvider
{
    private readonly ISiteContext _siteContext;
    private readonly IReadOnlyDictionary<string, IRegistrationFieldSet> _fieldSets;

    public RegistrationFieldSetProvider(
        ISiteContext siteContext, IEnumerable<IRegistrationFieldSet> fieldSets)
    {
        _siteContext = siteContext;
        _fieldSets = fieldSets.ToDictionary(set => set.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IRegistrationFieldSet Current
    {
        get
        {
            var key = _siteContext.Site.RegistrationFieldSet;

            // Failing loudly beats defaulting, and more so here than for ordering: a site
            // silently falling back to another store's field set would collect the wrong
            // paperwork, apply the wrong validation, and produce accounts nobody can approve
            // because the evidence they needed was never asked for.
            return _fieldSets.TryGetValue(key, out var fieldSet)
                ? fieldSet
                : throw new InvalidOperationException(
                    $"Site '{_siteContext.Site.SiteKey}' is configured with RegistrationFieldSet " +
                    $"'{key}', for which no IRegistrationFieldSet is registered. Known field sets: " +
                    $"{string.Join(", ", _fieldSets.Keys)}.");
        }
    }
}
