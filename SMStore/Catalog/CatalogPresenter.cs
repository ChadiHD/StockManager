using System.Globalization;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Pricing;
using SMStore.Sites;

namespace SMStore.Catalog;

/// <summary>
/// Turns a catalog request into something a page can render: runs the site-scoped query,
/// resolves a price per row, and formats it for the store's currency and locale.
///
/// Pages do not touch <see cref="ICatalogData"/> or <see cref="IPriceResolver"/> directly.
/// Price resolution and the decision to show a price at all are the two things most likely to
/// be got subtly wrong in a page, so they happen once, here.
/// </summary>
public sealed class CatalogPresenter
{
    private readonly ICatalogData _catalog;
    private readonly IPriceResolver _pricing;
    private readonly ISiteContext _siteContext;

    public CatalogPresenter(ICatalogData catalog, IPriceResolver pricing, ISiteContext siteContext)
    {
        _catalog = catalog;
        _pricing = pricing;
        _siteContext = siteContext;
    }

    /// <summary>
    /// Whether this viewer may see prices. Anonymous visitors on a store set to
    /// "Authenticated" see none — a trade-only store that leaks list prices to the open web
    /// has given away its position.
    /// </summary>
    public bool ShowPrices =>
        !string.Equals(_siteContext.Site.PriceDisplay, "Authenticated", StringComparison.OrdinalIgnoreCase)
        || CustomerGroupId is not null;

    /// <summary>
    /// The signed-in account's pricing group. Null until sign-in lands in T3, which means
    /// everyone currently sees list price and the group rules that apply to everyone.
    /// </summary>
    public int? CustomerGroupId => null;

    public CatalogResult Search(
        string? categorySlug, string? brand, bool inStockOnly, string? search, string? sort, int page)
    {
        var query = BuildQuery(categorySlug, brand, inStockOnly, search, sort, page);

        var result = _catalog.Search(query);

        // A page past the end comes back empty, and an empty page carries no total — the count
        // rides on the rows. Left alone, a stale deep link to /catalog?cat=x&page=5 renders
        // "0 products" and the no-matches empty state for a category that is full, and
        // TotalPages 0 removes the pager, so there is no link back either. Asking again is the
        // only way to recover the total; it costs a second query on a bad page number and
        // nothing at all on a normal browse.
        if (result.Items.Count == 0 && query.Page > 1)
        {
            query.Page = 1;
            result = _catalog.Search(query);

            if (result.TotalPages > 1)
            {
                query.Page = result.TotalPages;
                result = _catalog.Search(query);
            }
        }

        // Page is not one of the facet parameters, so the retries above do not disturb this.
        var facets = _catalog.GetFacets(query);

        return new CatalogResult(
            result.Items.Select(ToCard).ToList(),
            facets.Categories,
            facets.Brands,
            result.TotalCount,
            result.Page,
            result.TotalPages);
    }

    public CatalogItemModel? GetBySku(string sku)
        => _catalog.GetBySku(_siteContext.Site.Id, sku, CustomerGroupId);

    public IReadOnlyList<SiteCategoryModel> GetCategories()
        => _catalog.GetCategories(_siteContext.Site.Id).Where(c => c.IsActive).ToList();

    public ProductCardView ToCard(CatalogItemModel item) => new(
        item.Sku,
        item.ProductName,
        // Manufacturer is null across a whole feed whose field mapping has no manufacturer
        // column, so the card falls back to the distributor rather than showing a blank line.
        string.IsNullOrWhiteSpace(item.Manufacturer) ? item.Distributor ?? string.Empty : item.Manufacturer,
        item.Badge,
        Availability(item.QuantityInStock),
        item.QuantityInStock > 0,
        FormatPrice(item),
        string.IsNullOrWhiteSpace(item.ProductImage) ? null : item.ProductImage);

    /// <summary>Null when this viewer may not see prices, which the card renders differently.</summary>
    public string? FormatPrice(CatalogItemModel item)
    {
        if (!ShowPrices)
        {
            return null;
        }

        return Money(Resolve(item).NetPrice);
    }

    public ResolvedPrice Resolve(CatalogItemModel item) => _pricing.Resolve(
        item.RetailPrice,
        item.Cost,
        // Group discount arrives with sign-in; until then every viewer is on list.
        groupDiscountPct: 0m,
        minMarginPct: _siteContext.Site.MinMarginPct);

    public string Money(decimal amount)
    {
        var culture = ResolveCulture();

        return amount.ToString("C", culture);
    }

    /// <summary>
    /// The feed carries no lead time, so availability is derived from stock rather than
    /// invented. A distributor quantity is as fresh as the last sync, which is why nothing
    /// here promises a date.
    /// </summary>
    private static string Availability(int quantityInStock) =>
        quantityInStock > 0 ? "In stock" : "Lead time on request";

    private CatalogQuery BuildQuery(
        string? categorySlug, string? brand, bool inStockOnly, string? search, string? sort, int page)
        => new()
        {
            SiteId = _siteContext.Site.Id,
            CustomerGroupId = CustomerGroupId,
            CategorySlug = string.IsNullOrWhiteSpace(categorySlug) ? null : categorySlug,
            Brand = string.IsNullOrWhiteSpace(brand) ? null : brand,
            InStockOnly = inStockOnly,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Sort = string.IsNullOrWhiteSpace(sort) ? null : sort,
            // Mirrors the ceiling spCatalog_Search applies, so the page this reports back is
            // the page that was actually asked for.
            Page = page < 1 ? 1 : page > 100000 ? 100000 : page,
            PageSize = 24
        };

    private CultureInfo ResolveCulture()
    {
        CultureInfo culture;

        try
        {
            culture = CultureInfo.GetCultureInfo(_siteContext.Site.Locale ?? "en-IE");
        }
        catch (CultureNotFoundException)
        {
            // A misconfigured locale must not take the catalog down; fall back and let the
            // currency override below still get the symbol right.
            culture = CultureInfo.InvariantCulture;
        }

        var code = _siteContext.Site.CurrencyCode;

        if (string.IsNullOrWhiteSpace(code))
        {
            return culture;
        }

        // The site's currency wins over whatever the locale implies: a store can trade in a
        // currency that is not its locale's default, and showing the wrong symbol on a price
        // is worse than showing an unfamiliar format.
        var withCurrency = (CultureInfo)culture.Clone();
        withCurrency.NumberFormat.CurrencySymbol = CurrencySymbol(code);

        return withCurrency;
    }

    private static string CurrencySymbol(string code) => code.ToUpperInvariant() switch
    {
        "EUR" => "€",
        "GBP" => "£",
        "USD" => "$",
        _ => code + " "
    };
}

public sealed record CatalogResult(
    IReadOnlyList<ProductCardView> Items,
    IReadOnlyList<CatalogFacetModel> Categories,
    IReadOnlyList<CatalogFacetModel> Brands,
    int TotalCount,
    int Page,
    int TotalPages);
