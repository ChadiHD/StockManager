using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Sites;

namespace SMStore.Ordering;

/// <summary>
/// Turns a basket into a quote request.
/// </summary>
/// <remarks>
/// Prices are resolved here, at submit, from the product rows the basket read back — never
/// from the form. A price in a form is a client's opinion about what things cost, and the
/// browser has had the basket page open for as long as it likes. Resolving through
/// <see cref="CatalogPresenter"/> means the quote records what the customer was looking at,
/// through the same path that rendered it.
///
/// A signed-in contact is required, and that is worth stating because
/// <c>IOrderingMode.RequiresApprovedAccount</c> reads false for RFQ. The two are not in
/// conflict: <c>Quote.AccountId</c> is NOT NULL, so a request needs an account whatever the
/// mode says, and the property is about whether that account must already be *approved*. As it
/// happens nothing can reach an unapproved session either — <c>CustomerAuthEndpoints</c> and
/// <c>CustomerSessionValidator</c> both admit Approved accounts only — so the property is
/// currently unreachable rather than honoured. It stays because a store that wants a pending
/// applicant to be able to ask for a price will change the sign-in gate, not this.
/// </remarks>
public sealed class QuoteSubmissionService
{
    private readonly IQuoteData _quotes;
    private readonly IBasketData _baskets;
    private readonly CatalogPresenter _catalog;
    private readonly ISiteContext _siteContext;
    private readonly ICustomerContext _customer;
    private readonly ILogger<QuoteSubmissionService> _logger;

    public QuoteSubmissionService(
        IQuoteData quotes,
        IBasketData baskets,
        CatalogPresenter catalog,
        ISiteContext siteContext,
        ICustomerContext customer,
        ILogger<QuoteSubmissionService> logger)
    {
        _quotes = quotes;
        _baskets = baskets;
        _catalog = catalog;
        _siteContext = siteContext;
        _customer = customer;
        _logger = logger;
    }

    public QuoteSubmission Submit(int basketId, string? customerNote)
    {
        var contact = _customer.Contact;

        if (contact is null)
        {
            return QuoteSubmission.NotSignedIn;
        }

        var siteId = _siteContext.Site.Id;
        var lines = _baskets.GetLines(basketId, siteId, _customer.CustomerGroupId);

        // Unavailable lines are dropped rather than refused. The basket page has already warned
        // about them, and a submit that refuses until the customer tidies up puts the store's
        // supply problem in their way at the moment they were ready to buy. What must not happen
        // is dropping them silently, so the acknowledgement names them.
        var available = lines.Where(line => line.Available).ToList();
        var dropped = lines.Where(line => !line.Available).Select(line => line.Sku).ToList();

        if (available.Count == 0)
        {
            return QuoteSubmission.NothingToQuote(dropped);
        }

        var request = new QuoteRequest
        {
            ContactId = contact.Id,
            SiteId = siteId,
            CustomerNote = Trim(customerNote),
            BasketId = basketId,
            Lines = available.Select(ToRequestLine).ToList()
        };

        try
        {
            var quote = _quotes.SubmitRequest(request);

            if (quote is null)
            {
                _logger.LogError(
                    "A quote request for contact {ContactId} at {SiteKey} wrote no quote.",
                    contact.Id, _siteContext.Site.SiteKey);

                return QuoteSubmission.Failed;
            }

            _logger.LogInformation(
                "Quote {Reference} requested at {SiteKey} with {Lines} lines.",
                quote.Reference, _siteContext.Site.SiteKey, request.Lines.Count);

            return QuoteSubmission.Submitted(quote.Reference, dropped);
        }
        catch (Exception exception)
        {
            // The procedure is all-or-nothing, so a failure here leaves no quote and a basket
            // the customer still has. Reported as a failure they can retry rather than as an
            // error page, because retrying is the right next move.
            _logger.LogError(exception,
                "Could not submit a quote request for contact {ContactId} at {SiteKey}.",
                contact.Id, _siteContext.Site.SiteKey);

            return QuoteSubmission.Failed;
        }
    }

    private QuoteRequestLine ToRequestLine(BasketLineModel line)
    {
        var resolved = _catalog.Resolve(line.RetailPrice, line.Cost);

        return new QuoteRequestLine
        {
            ProductId = line.ProductId,
            Quantity = line.Quantity,
            ListPrice = resolved.ListPrice,
            // The effective discount, which is not the group's rate when the site's margin
            // floor bound. QuoteLine.DiscountPct is DECIMAL(5, 2) so it survives being stored,
            // and NetPrice is sent alongside rather than re-derived from it.
            DiscountPct = resolved.EffectiveDiscountPct,
            NetPrice = resolved.NetPrice
        };
    }

    private static string? Trim(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

/// <summary>
/// What came of a submit.
/// </summary>
/// <remarks>
/// Four outcomes rather than a bool, because three of them need different words on the page and
/// only one of them is a fault. Shaped like <c>QuoteAcceptanceResult</c> for the same reason.
/// </remarks>
public sealed class QuoteSubmission
{
    private QuoteSubmission(
        string? reference, bool signedIn, bool hadAnythingToQuote, IReadOnlyList<string> dropped)
    {
        Reference = reference;
        SignedIn = signedIn;
        HadAnythingToQuote = hadAnythingToQuote;
        DroppedSkus = dropped;
    }

    /// <summary>The quote that now exists, or null.</summary>
    public string? Reference { get; }

    public bool SignedIn { get; }

    /// <summary>False when every line in the basket had become unavailable.</summary>
    public bool HadAnythingToQuote { get; }

    /// <summary>
    /// Lines left out because the store can no longer supply them. Named on the
    /// acknowledgement, because dropping them silently is the one thing this must not do.
    /// </summary>
    public IReadOnlyList<string> DroppedSkus { get; }

    public bool Succeeded => Reference is not null;

    public static readonly QuoteSubmission NotSignedIn = new(null, false, true, []);

    public static readonly QuoteSubmission Failed = new(null, true, true, []);

    public static QuoteSubmission NothingToQuote(IReadOnlyList<string> dropped) =>
        new(null, true, false, dropped);

    public static QuoteSubmission Submitted(string reference, IReadOnlyList<string> dropped) =>
        new(reference, true, true, dropped);
}
