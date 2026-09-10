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
    public string AddToBasketLabel => "Add to quote";
    public string BasketRoute => "/quote";
    public string SubmitBasketLabel => "Submit quote request";

    // An RFQ is a conversation opener, so anyone may send one; qualification happens when the
    // account application is reviewed.
    public bool RequiresApprovedAccount => false;

    public bool ShowsPayableTotal => false;
}
