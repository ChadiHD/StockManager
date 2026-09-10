namespace SMStore.Catalog;

/// <summary>
/// What a product tile needs, already formatted. A presentation record rather than the catalog
/// row itself: price formatting depends on the site's currency and on whether the viewer may
/// see prices at all, and that decision belongs upstream of the component, made once.
///
/// The catalog query in T2 projects onto this.
/// </summary>
/// <param name="PriceLabel">
/// Already formatted, or null when the site hides prices from this viewer. The card renders the
/// row differently rather than showing a zero.
/// </param>
public sealed record ProductCardView(
    string Sku,
    string Title,
    string Brand,
    string? Badge,
    string Availability,
    bool InStock,
    string? PriceLabel,
    string? ImageUrl)
{
    public string Href => $"/product/{Uri.EscapeDataString(Sku)}";
}
