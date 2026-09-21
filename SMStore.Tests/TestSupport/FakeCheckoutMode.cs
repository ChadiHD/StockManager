using SMStore.Ordering;

namespace SMStore.Tests.TestSupport;

/// <summary>
/// A second ordering mode, so a page can be rendered as a store that is not aclitrade.
/// </summary>
/// <remarks>
/// <c>RfqOrderingMode</c> is the only implementation the platform ships, which means every
/// storefront test renders the same words and a page that hard-coded "quote" would pass all of
/// them. This is the counterfactual: deliberately different words and the two booleans
/// inverted, so a page that reads its labels from <c>IOrderingMode</c> renders "cart"
/// throughout and one that wrote them down does not.
///
/// It is not a preview of <c>DirectCheckout</c> — that lands with card payment and will have
/// behaviour, not just labels. This only has to differ.
/// </remarks>
public sealed class FakeCheckoutMode : IOrderingMode
{
    public string Key => "DirectCheckout";
    public string AddToBasketLabel => "Add to cart";
    public string BasketRoute => "/cart";
    public string BasketLabel => "Cart";
    public string SubmitBasketLabel => "Checkout";

    public bool RequiresApprovedAccount => true;

    public bool ShowsPayableTotal => true;
}
