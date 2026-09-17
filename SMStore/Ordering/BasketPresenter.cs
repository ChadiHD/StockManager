using SMDataManager.Library.Models;
using SMStore.Catalog;

namespace SMStore.Ordering;

/// <summary>
/// Turns a basket into something a page can render: resolves a price per line, formats it for
/// the store, and totals it.
/// </summary>
/// <remarks>
/// The basket's counterpart to <see cref="CatalogPresenter"/>, and here for the same two
/// reasons. Prices go through <c>CatalogPresenter.Resolve</c> so the basket cannot disagree
/// with the page the customer added from — a line that changed price between the catalog and
/// the basket is a line they will argue about — and <see cref="BasketLineModel.Cost"/> stops
/// here, because it is a buy price and <see cref="BasketLineView"/> has no field for it.
///
/// Item 5 submits from the same resolved lines, so the price quoted is the price rendered.
/// </remarks>
public sealed class BasketPresenter
{
    private readonly BasketService _basket;
    private readonly CatalogPresenter _catalog;
    private readonly OrderingModeProvider _ordering;

    public BasketPresenter(
        BasketService basket, CatalogPresenter catalog, OrderingModeProvider ordering)
    {
        _basket = basket;
        _catalog = catalog;
        _ordering = ordering;
    }

    public BasketView Read()
    {
        var lines = _basket.Lines();

        var views = lines.Select(ToView).ToList();

        // Only over lines that can actually be supplied. A total that silently counted a
        // delisted line would be a number the customer is then told is wrong.
        var total = lines
            .Where(line => line.Available)
            .Sum(line => line.Quantity * _catalog.Resolve(line.RetailPrice, line.Cost).NetPrice);

        return new BasketView(
            views,
            views.Count,
            lines.Sum(line => line.Quantity),
            // Under RFQ there is no payable total, so the basket shows an indicative value
            // instead and the page words it that way. ShowsPayableTotal is what decides which.
            _catalog.ShowPrices ? _catalog.Money(total) : null,
            views.Any(line => !line.Available));
    }

    /// <summary>The words this store uses for its basket, so pages read none of them.</summary>
    public IOrderingMode Mode => _ordering.Current;

    private BasketLineView ToView(BasketLineModel line)
    {
        var unit = _catalog.Resolve(line.RetailPrice, line.Cost).NetPrice;

        return new BasketLineView(
            line.Sku,
            line.Name,
            line.Quantity,
            _catalog.ShowPrices ? _catalog.Money(unit) : null,
            _catalog.ShowPrices ? _catalog.Money(unit * line.Quantity) : null,
            line.Available,
            // The feed carries no lead time, so this says what stock says and nothing more —
            // the same wording the catalog uses, for the same reason.
            line.QuantityInStock > 0 ? "In stock" : "Lead time on request");
    }
}

/// <summary>
/// One basket line as a page renders it.
/// </summary>
/// <remarks>
/// No <c>Cost</c> and no <c>ProductId</c>. The first is a buy price and this record is the
/// boundary that keeps it off a public page; the second is a database id, and the forms on the
/// basket page post the SKU instead so none ever appears in storefront markup.
/// </remarks>
public sealed record BasketLineView(
    string Sku,
    string Name,
    int Quantity,
    string? UnitPrice,
    string? LineTotal,
    bool Available,
    string Availability);

public sealed record BasketView(
    IReadOnlyList<BasketLineView> Lines,
    int LineCount,
    int TotalUnits,
    string? Total,
    bool HasUnavailableLines)
{
    public bool IsEmpty => Lines.Count == 0;
}
