using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Sites;

namespace SMStore.Ordering;

/// <summary>
/// The customer accepting or rejecting one of their own quotes.
/// </summary>
/// <remarks>
/// Accepting goes through <c>spOrder_ConvertFromQuote</c>, the same procedure the admin's
/// Convert button uses — one procedure, one guard. What differs is what is checked before it:
/// this path requires the quote to be `Priced` and unexpired, because a customer accepting a
/// price nobody has set is not the same deliberate act as an admin converting an unpriced quote
/// for somebody who rang up. Those rules live here rather than in the procedure for exactly
/// that reason.
///
/// The placer is the contact from the session. <c>Purchase.StaffId</c> is a foreign key into
/// <c>dbo.[User]</c>, which holds staff, so a customer acceptance records
/// <c>PlacedByContactId</c> instead and <c>CK_Purchase_Placer</c> refuses a row with both or
/// neither.
/// </remarks>
public sealed class QuoteDecisionService
{
    private readonly IQuoteData _quotes;
    private readonly IOrderData _orders;
    private readonly ISiteContext _siteContext;
    private readonly ICustomerContext _customer;
    private readonly ILogger<QuoteDecisionService> _logger;

    public QuoteDecisionService(
        IQuoteData quotes,
        IOrderData orders,
        ISiteContext siteContext,
        ICustomerContext customer,
        ILogger<QuoteDecisionService> logger)
    {
        _quotes = quotes;
        _orders = orders;
        _siteContext = siteContext;
        _customer = customer;
        _logger = logger;
    }

    public QuoteDecision Accept(string? reference, string? poNumber)
    {
        var quote = Resolve(reference);

        if (quote is null)
        {
            return QuoteDecision.NotYours;
        }

        if (!IsDecidable(quote))
        {
            return QuoteDecision.NotDecidable;
        }

        var outcome = _orders.ConvertQuoteToOrder(
            quote.Id,
            QuoteAcceptance.ByCustomer(_customer.Contact!.Id, poNumber),
            _siteContext.Site.Id);

        if (outcome.NoLongerAwaitingAcceptance)
        {
            // A colleague at the same company decided it while this page was open, or an admin
            // converted it. Ordinary for a company with two buyers, so the page shows the
            // quote's real state rather than an error.
            return QuoteDecision.AlreadyDecided;
        }

        if (!outcome.Succeeded)
        {
            _logger.LogError(
                "Accepting quote {Reference} at {SiteKey} produced no order.",
                quote.Reference, _siteContext.Site.SiteKey);

            return QuoteDecision.Failed;
        }

        _logger.LogInformation(
            "Quote {Reference} accepted by a customer at {SiteKey}, order {Order}.",
            quote.Reference, _siteContext.Site.SiteKey, outcome.Order!.Reference);

        return QuoteDecision.Accepted(outcome.Order.Reference);
    }

    public QuoteDecision Reject(string? reference, string? reason)
    {
        var quote = Resolve(reference);

        if (quote is null)
        {
            return QuoteDecision.NotYours;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            // Required by the procedure too, and asked for here so the customer gets a form
            // back rather than an error. "They said no" is not an answer to anybody asking
            // what went wrong with the price.
            return QuoteDecision.ReasonRequired;
        }

        if (!IsDecidable(quote))
        {
            return QuoteDecision.NotDecidable;
        }

        try
        {
            var rejected = _quotes.RejectForAccount(
                quote.Id, _customer.AccountId!.Value, _siteContext.Site.Id, reason);

            if (!rejected)
            {
                return QuoteDecision.AlreadyDecided;
            }

            _logger.LogInformation(
                "Quote {Reference} rejected by a customer at {SiteKey}.",
                quote.Reference, _siteContext.Site.SiteKey);

            return QuoteDecision.Rejected;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Could not reject quote {Reference} at {SiteKey}.",
                quote.Reference, _siteContext.Site.SiteKey);

            return QuoteDecision.Failed;
        }
    }

    /// <summary>
    /// The quote, if it is this account's. Null for "not yours" and "not here" alike.
    /// </summary>
    /// <remarks>
    /// References are sequential, so a distinguishable refusal would confirm which ones exist —
    /// the reason <c>AccountDocumentController</c> answers 404 rather than 403.
    /// </remarks>
    private QuoteModel? Resolve(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || _customer.AccountId is not { } accountId)
        {
            return null;
        }

        return _quotes.GetQuoteForAccount(reference.Trim(), accountId, _siteContext.Site.Id);
    }

    /// <summary>Priced, and not past the date the store put in writing.</summary>
    private static bool IsDecidable(QuoteModel quote) =>
        string.Equals(quote.Status, QuoteStatus.Priced, StringComparison.Ordinal)
        && (quote.ExpiresDate is null || quote.ExpiresDate >= DateTime.UtcNow);
}

/// <summary>
/// What came of a customer's decision.
/// </summary>
/// <remarks>
/// Six outcomes, and only one of them is a fault. The others each need different words: a
/// quote that is not yours, one nobody has priced yet, one a colleague already decided, and a
/// rejection with no reason are all things the customer can act on, and a single "that did not
/// work" would tell them nothing about which.
/// </remarks>
public sealed class QuoteDecision
{
    private QuoteDecision(string name, string? orderReference = null)
    {
        Name = name;
        OrderReference = orderReference;
    }

    public string Name { get; }

    /// <summary>The order an acceptance produced, or null.</summary>
    public string? OrderReference { get; }

    public bool Succeeded => Name is "Accepted" or "Rejected";

    public static readonly QuoteDecision NotYours = new("NotYours");
    public static readonly QuoteDecision NotDecidable = new("NotDecidable");
    public static readonly QuoteDecision AlreadyDecided = new("AlreadyDecided");
    public static readonly QuoteDecision ReasonRequired = new("ReasonRequired");
    public static readonly QuoteDecision Failed = new("Failed");
    public static readonly QuoteDecision Rejected = new("Rejected");

    public static QuoteDecision Accepted(string orderReference) =>
        new("Accepted", orderReference);
}
