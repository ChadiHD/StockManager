using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Pricing;
using SMStore.Accounts;
using SMStore.Sites;
using static SMStore.Localization.StoreText;

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
    private readonly ICustomerContext _customer;

    public CatalogPresenter(
        ICatalogData catalog,
        IPriceResolver pricing,
        ISiteContext siteContext,
        ICustomerContext customer)
    {
        _catalog = catalog;
        _pricing = pricing;
        _siteContext = siteContext;
        _customer = customer;
    }

    /// <summary>
    /// Whether this viewer may see prices. Anonymous visitors on a store set to
    /// "Authenticated" see none — a trade-only store that leaks list prices to the open web
    /// has given away its position.
    /// </summary>
    /// <remarks>
    /// Signed in, not "has a pricing group". This used to test the group, and a group is
    /// optional — approval keeps whatever the account already had, which may be nothing — so
    /// an approved customer nobody had grouped yet was shown no prices at all. The group
    /// decides which price a customer sees; it never decides whether they see one.
    /// </remarks>
    public bool ShowPrices =>
        !string.Equals(_siteContext.Site.PriceDisplay, "Authenticated", StringComparison.OrdinalIgnoreCase)
        || _customer.IsSignedIn;

    /// <summary>
    /// The sort the query runs, given the one asked for. Null means the default.
    /// </summary>
    /// <remarks>
    /// A price sort is dropped for a viewer who may not see prices. The cards hide the amount,
    /// but the order alone would still rank the products by price for anybody who asked —
    /// and the request is a query string anyone can write, so hiding the link is not enough.
    /// </remarks>
    public string? SortFor(string? requested) =>
        string.IsNullOrWhiteSpace(requested)
        || (!ShowPrices && requested.StartsWith("price-", StringComparison.OrdinalIgnoreCase))
            ? null
            : requested;

    /// <summary>
    /// The signed-in account's pricing group, or null for an anonymous visitor — who sees
    /// list price and only the visibility rules that apply to everyone.
    /// </summary>
    /// <remarks>
    /// From the resolved session and nowhere else. This value decides both the discount and
    /// which products exist as far as this viewer is concerned, so a query string or a form
    /// field reaching it would be a discount anyone could ask for. The procedures defend
    /// themselves too — a group that does not belong to the request's site is treated as no
    /// group — but that is the second line, not the first.
    /// </remarks>
    public int? CustomerGroupId => _customer.CustomerGroupId;

    public CatalogResult Search(
        string? categorySlug, string? brand, bool inStockOnly, string? search, string? sort, int page,
        IReadOnlyList<CatalogOption>? options = null)
    {
        var query = BuildQuery(categorySlug, brand, inStockOnly, search, sort, page);
        query.Options = options?.ToList() ?? [];

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
            result.TotalPages,
            facets.Options);
    }

    /// <summary>
    /// The top of the catalog's default order, which puts the store's featured products first,
    /// for the home page (T9). One query and no facets: the home page is the most-visited page
    /// and shows neither a count nor a filter.
    /// </summary>
    public IReadOnlyList<ProductCardView> Featured(int count)
    {
        var query = BuildQuery(null, null, false, null, null, 1);
        query.PageSize = count;

        return _catalog.Search(query).Items.Select(ToCard).ToList();
    }

    /// <summary>
    /// The option filter a URL carries (T11): <c>?opt=Size:M&amp;opt=Colour:Navy</c>.
    /// </summary>
    /// <remarks>
    /// The values arrive in a URL anybody can write, and they become a table-valued parameter
    /// keyed on the pair with 50-character columns. So a repeat is dropped rather than failing
    /// the key, an oversized or malformed one is dropped rather than failing the truncation
    /// check, and the count is capped. Either failure would be an unauthenticated 500 from one
    /// query string. A dropped value filters by nothing, which is what a mistyped URL meant.
    /// </remarks>
    public static IReadOnlyList<CatalogOption> ParseOptions(IEnumerable<string?>? raw) =>
        (raw ?? [])
            .Select(ParseOption)
            .OfType<CatalogOption>()
            .Distinct()
            .Take(MaxOptions)
            .ToList();

    /// <summary>How an option travels in <c>?opt=</c>. A name may not contain the colon.</summary>
    public static string Encode(CatalogOption option) => $"{option.Name}:{option.Value}";

    private const int MaxOptions = 20;

    private const int MaxOptionLength = 50;

    private static CatalogOption? ParseOption(string? raw)
    {
        var colon = raw?.IndexOf(':') ?? -1;
        if (colon < 1)
        {
            return null;
        }

        var name = raw![..colon].Trim();
        var value = raw[(colon + 1)..].Trim();

        return name.Length is > 0 and <= MaxOptionLength && value.Length is > 0 and <= MaxOptionLength
            ? new CatalogOption(name, value)
            : null;
    }

    public CatalogItemModel? GetBySku(string sku)
        => _catalog.GetBySku(_siteContext.Site.Id, sku, CustomerGroupId);

    /// <summary>
    /// The page for a SKU (T11). A variant's SKU opens its style's page with that variant chosen,
    /// so a link from a basket line or a quote lands where the size can be changed. Null when
    /// nothing is on sale here, a variant of a style the store hid included.
    /// </summary>
    public ProductPage? GetProductPage(string sku)
    {
        var item = GetBySku(sku);

        if (item is null)
        {
            return null;
        }

        if (item.ParentProductId is null && !item.HasVariants)
        {
            return new ProductPage(item, [], null, null);
        }

        var family = _catalog.GetFamily(_siteContext.Site.Id, item.ParentProductId ?? item.Id, CustomerGroupId);

        if (family.Style is null)
        {
            return null;
        }

        // Values in the order fnProduct_VariantLabels writes them into the variant's name, so the
        // choice here and the line in the basket read the same.
        string LabelOf(int id) => string.Join(" / ", family.Options
            .Where(option => option.ProductId == id)
            .OrderBy(option => option.SortOrder).ThenBy(option => option.Name, StringComparer.Ordinal)
            .Select(option => option.Value));

        var names = family.Options
            .GroupBy(option => option.Name)
            .OrderBy(group => group.Min(option => option.SortOrder)).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .ToList();

        return new ProductPage(
            family.Style,
            family.Variants.Select(variant => new VariantChoice(variant.Sku, LabelOf(variant.Id), variant.QuantityInStock > 0)).ToList(),
            item.ParentProductId is null ? null : item.Sku,
            names.Count == 0 ? null : string.Join(" / ", names));
    }

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
        string.IsNullOrWhiteSpace(item.ProductImage) ? null : item.ProductImage,
        item.HasVariants);

    /// <summary>Null when this viewer may not see prices, which the card renders differently.</summary>
    public string? FormatPrice(CatalogItemModel item)
    {
        if (!ShowPrices)
        {
            return null;
        }

        return Money(Resolve(item).NetPrice);
    }

    /// <summary>
    /// The price this viewer is shown.
    /// </summary>
    /// <remarks>
    /// Must agree with the net price <c>dbo.fnCatalog_VisibleProducts</c> computed to sort
    /// the page, or a price-sorted list renders visibly out of order. Both are driven by the
    /// same three inputs — the row, this group's rate and this site's margin floor — and
    /// <c>CatalogPriceParityTests</c> is what keeps the two implementations of the rule
    /// agreeing on what to do with them.
    /// </remarks>
    public ResolvedPrice Resolve(CatalogItemModel item) => Resolve(item.RetailPrice, item.Cost);

    /// <summary>
    /// The same resolution for a row that is not a catalog item.
    /// </summary>
    /// <remarks>
    /// A basket line carries a list price and a cost and nothing else the rule needs, but it
    /// is not a <see cref="CatalogItemModel"/>. This overload exists so the basket resolves
    /// prices through the same two inputs as the catalog rather than reaching
    /// <c>IPriceResolver</c> itself, which is the thing pages are forbidden to do.
    /// </remarks>
    public ResolvedPrice Resolve(decimal listPrice, decimal? cost) => _pricing.Resolve(
        listPrice,
        cost,
        groupDiscountPct: _customer.GroupDiscountPct,
        minMarginPct: _siteContext.Site.MinMarginPct);

    // Through SiteMoney, which the mail templates use too, so a total on a page and the same
    // total in a confirmation are written by one rule.
    public string Money(decimal amount) => SiteMoney.Format(_siteContext.Site, amount);

    /// <summary>
    /// The feed carries no lead time, so availability is derived from stock rather than
    /// invented. A distributor quantity is as fresh as the last sync, which is why nothing
    /// here promises a date.
    /// </summary>
    private static string Availability(int quantityInStock) =>
        quantityInStock > 0 ? L["In stock"] : L["Lead time on request"];

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
            Sort = SortFor(sort),
            // Mirrors the ceiling spCatalog_Search applies, so the page this reports back is
            // the page that was actually asked for.
            Page = page < 1 ? 1 : page > 100000 ? 100000 : page,
            PageSize = 24
        };
}

public sealed record CatalogResult(
    IReadOnlyList<ProductCardView> Items,
    IReadOnlyList<CatalogFacetModel> Categories,
    IReadOnlyList<CatalogFacetModel> Brands,
    int TotalCount,
    int Page,
    int TotalPages,
    IReadOnlyList<CatalogOptionFacetModel> Options);
