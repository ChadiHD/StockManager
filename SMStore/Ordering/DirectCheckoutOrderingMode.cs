using static SMStore.Localization.StoreText;

namespace SMStore.Ordering;

/// <summary>
/// Checkout ordering (T12): the cart is the order, at the prices it shows, placed at
/// <c>/checkout</c> by a guest or a signed-in customer. Payment follows in T13; until then an
/// order waits at <c>Awaiting payment</c>.
/// </summary>
public sealed class DirectCheckoutOrderingMode : IOrderingMode
{
    public const string ModeKey = "DirectCheckout";

    public string Key => ModeKey;
    public string AddToBasketLabel => L["Add to cart"];
    public string BasketRoute => "/cart";
    public string BasketLabel => L["Cart"];
    public string BasketNoun => L["cart"];
    public string SubmitBasketLabel => L["Checkout"];
    public bool TakesQuotes => false;
    public bool ShowsPayableTotal => true;
}
