namespace SMStore.Ordering;

/// <summary>
/// How a store turns a basket into an order. aclitrade quotes everything through sales; a
/// future store may take an order straight from the cart. The seam exists from the start
/// because retrofitting it once the quote flow is woven through the storefront is structural
/// surgery, not a refactor.
///
/// <see cref="RfqOrderingMode"/> quotes, and <see cref="DirectCheckoutOrderingMode"/> (T12) takes
/// an order from the cart. Nothing outside this folder should branch on <c>Site.OrderMode</c>.
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

    /// <summary>
    /// The same noun inside a sentence — "quote", "cart" — for "Your {0} is empty" and the like
    /// (T10). A word of its own rather than <see cref="BasketLabel"/> lowercased, because a
    /// language that capitalises its nouns, or whose label is not one word, cannot be lowercased
    /// into a sentence.
    /// </summary>
    string BasketNoun { get; }

    /// <summary>Text on the control that submits the basket.</summary>
    string SubmitBasketLabel { get; }

    /// <summary>
    /// Whether the basket becomes a quote request (T12). True for RFQ: the basket submits one,
    /// and the account area lists quotes. False for a checkout store: the basket leads to
    /// <c>/checkout</c>, and there are no quotes to list.
    /// </summary>
    bool TakesQuotes { get; }

    /// <summary>
    /// Whether the basket can show a payable total. Under RFQ it cannot — the price is not
    /// firm until sales returns the quote.
    /// </summary>
    bool ShowsPayableTotal { get; }
}
