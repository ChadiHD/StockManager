namespace SMStore.Ordering;

/// <summary>
/// How a store turns a basket into an order. aclitrade quotes everything through sales; a
/// future store may take an order straight from the cart. The seam exists from the start
/// because retrofitting it once the quote flow is woven through the storefront is structural
/// surgery, not a refactor.
///
/// Only <see cref="RfqOrderingMode"/> exists today. A DirectCheckout implementation lands with
/// card payment; nothing outside this folder should branch on <c>Site.OrderMode</c>.
/// </summary>
public interface IOrderingMode
{
    /// <summary>Matches the value stored in <c>dbo.Site.OrderMode</c>.</summary>
    string Key { get; }

    /// <summary>Button text on a product page — "Add to quote" rather than "Add to cart".</summary>
    string AddToBasketLabel { get; }

    /// <summary>Where the basket lives, e.g. "/quote" or "/cart".</summary>
    string BasketRoute { get; }

    /// <summary>
    /// Short noun for the basket itself — "Quote", "Cart". Names the header button and the
    /// basket page, where <see cref="AddToBasketLabel"/> would read as an instruction.
    /// </summary>
    string BasketLabel { get; }

    /// <summary>Text on the control that submits the basket.</summary>
    string SubmitBasketLabel { get; }

    /// <summary>
    /// Whether a customer must belong to an approved account before submitting. RFQ stores
    /// accept requests from anyone and qualify them afterwards; a checkout store cannot.
    /// </summary>
    bool RequiresApprovedAccount { get; }

    /// <summary>
    /// Whether the basket can show a payable total. Under RFQ it cannot — the price is not
    /// firm until sales returns the quote.
    /// </summary>
    bool ShowsPayableTotal { get; }
}
