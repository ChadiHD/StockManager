using SMStore.Ordering;
using SMStore.Sites;

namespace SMStore.Navigation;

public sealed record NavItem(string Label, string Href);

public sealed record NavGroup(string Heading, IReadOnlyList<NavItem> Links);

/// <summary>
/// The store's own navigation. Assembled here rather than written into the header and footer
/// markup so that a site can vary it, and so the basket entry follows the site's ordering mode
/// instead of assuming a quote flow.
///
/// The link sets below are the platform defaults. Per-site overrides arrive with the content
/// mechanism later in T1; nothing here is store-specific, so a second store inherits a working
/// menu with no configuration.
/// </summary>
public sealed class StoreNavigation
{
    private readonly ISiteContext _siteContext;
    private readonly OrderingModeProvider _ordering;

    public StoreNavigation(ISiteContext siteContext, OrderingModeProvider ordering)
    {
        _siteContext = siteContext;
        _ordering = ordering;
    }

    public IReadOnlyList<NavItem> Primary { get; } =
    [
        new("Home", "/"),
        new("Catalog", "/catalog"),
        new("Solutions", "/solutions"),
    ];

    public NavItem SignIn { get; } = new("Sign in", "/login");

    public NavItem Register { get; } = new("Open an account", "/register");

    /// <summary>
    /// The basket. Label and route come from the ordering mode — "Add to quote" stores send
    /// people to /quote, a checkout store would send them to /cart — so no caller has to know
    /// which kind of store it is rendering.
    /// </summary>
    public NavItem Basket => new(_ordering.Current.BasketLabel, _ordering.Current.BasketRoute);

    public IReadOnlyList<NavGroup> FooterGroups { get; } =
    [
        new("Catalog",
        [
            new NavItem("All products", "/catalog"),
            new NavItem("Solutions", "/solutions"),
        ]),
        new("Company",
        [
            new NavItem("About", "/about"),
            new NavItem("Contact", "/contact"),
        ]),
        new("Account",
        [
            new NavItem("Sign in", "/login"),
            new NavItem("Open an account", "/register"),
        ]),
    ];

    /// <summary>Shown in the footer's legal strip alongside the copyright.</summary>
    public IReadOnlyList<NavItem> Legal { get; } =
    [
        new("Terms", "/terms"),
        new("Privacy", "/privacy"),
    ];

    public string StoreName => _siteContext.Site.Name;
}
