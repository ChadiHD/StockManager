using static SMStore.Localization.StoreText;

namespace SMStore.Ordering;

/// <summary>
/// Request-for-quote ordering: the customer assembles a list, sales prices it, the customer
/// accepts, and only then does an order exist. Prices shown before that point are list prices,
/// never a payable total.
/// </summary>
public sealed class RfqOrderingMode : IOrderingMode
{
    public const string ModeKey = "Rfq";

    public string Key => ModeKey;
    public string AddToBasketLabel => L["Add to quote"];
    public string BasketRoute => "/quote";
    public string BasketLabel => L["Quote"];
    public string BasketNoun => L["quote"];
    public string SubmitBasketLabel => L["Submit quote request"];

    public bool TakesQuotes => true;

    public bool ShowsPayableTotal => false;
}
