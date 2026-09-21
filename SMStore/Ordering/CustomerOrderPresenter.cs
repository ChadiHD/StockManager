using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Sites;

namespace SMStore.Ordering;

/// <summary>
/// A customer's own quotes and orders, and the two decisions they can make about a quote.
/// </summary>
/// <remarks>
/// Every read here is scoped by account as well as by site, and the account comes from the
/// session — never from a route parameter. That is the same rule that means there is no
/// <c>/account/{id}</c>, applied to documents that carry prices: a reference comes from a
/// sequence and reads QT-0041, so scoping by site alone would let any signed-in customer read
/// any other customer's quote by changing a digit. The procedures repeat the predicate rather
/// than trusting this class, because defence that depends on call order survives until somebody
/// adds a second caller.
///
/// Prices come back already resolved, because a quote line stores what the customer was shown
/// at submit. There is nothing to resolve: <c>PriceResolver</c> ran once, at submit, and the
/// numbers on the quote are the numbers sales priced. Re-resolving here would re-price a
/// document the store has already committed to.
/// </remarks>
public sealed class CustomerOrderPresenter
{
    private readonly IQuoteData _quotes;
    private readonly IOrderData _orders;
    private readonly IAccountData _accounts;
    private readonly CatalogPresenter _catalog;
    private readonly ISiteContext _siteContext;
    private readonly ICustomerContext _customer;
    private readonly OrderingModeProvider _ordering;
    private readonly ILogger<CustomerOrderPresenter> _logger;

    public CustomerOrderPresenter(
        IQuoteData quotes,
        IOrderData orders,
        IAccountData accounts,
        CatalogPresenter catalog,
        ISiteContext siteContext,
        ICustomerContext customer,
        OrderingModeProvider ordering,
        ILogger<CustomerOrderPresenter> logger)
    {
        _quotes = quotes;
        _orders = orders;
        _accounts = accounts;
        _catalog = catalog;
        _siteContext = siteContext;
        _customer = customer;
        _ordering = ordering;
        _logger = logger;
    }

    public IOrderingMode Mode => _ordering.Current;

    /// <summary>
    /// Who a printed document is addressed to, or null when nobody is signed in.
    /// </summary>
    /// <remarks>
    /// The company comes from the account the session resolves to, read with the site as well
    /// as the id — the same predicate every other read here carries, for the same reason.
    /// Only the printable pages need it, so it is fetched on demand rather than added to every
    /// quote and order view.
    /// </remarks>
    public DocumentParty? Party()
    {
        if (_customer.AccountId is not { } accountId || _customer.Contact is not { } contact)
        {
            return null;
        }

        var account = _accounts.GetAccountById(accountId, _siteContext.Site.Id);

        return account is null
            ? null
            : new DocumentParty(
                account.Company,
                $"{contact.FirstName} {contact.LastName}".Trim(),
                contact.Email);
    }

    public IReadOnlyList<DocumentSummary> Quotes()
    {
        if (_customer.AccountId is not { } accountId)
        {
            return [];
        }

        return _quotes.GetQuotesForAccount(accountId, _siteContext.Site.Id)
            .Select(quote => new DocumentSummary(
                quote.Reference,
                quote.Status,
                quote.CreatedDate,
                quote.Lines,
                Money(quote.Value),
                Expiry(quote)))
            .ToList();
    }

    public IReadOnlyList<DocumentSummary> Orders()
    {
        if (_customer.AccountId is not { } accountId)
        {
            return [];
        }

        return _orders.GetOrdersForAccount(accountId, _siteContext.Site.Id)
            .Select(order => new DocumentSummary(
                order.Reference,
                order.Status,
                order.PurchaseDate,
                order.Items,
                Money(order.FinalPrice),
                order.PoNumber is null ? null : $"Your reference {order.PoNumber}"))
            .ToList();
    }

    /// <summary>One quote and its lines, or null when it is not this account's.</summary>
    public QuoteView? Quote(string reference)
    {
        if (_customer.AccountId is not { } accountId)
        {
            return null;
        }

        var siteId = _siteContext.Site.Id;
        var quote = _quotes.GetQuoteForAccount(reference, accountId, siteId);

        if (quote is null)
        {
            return null;
        }

        var lines = _quotes.GetQuoteLinesForAccount(quote.Id, accountId, siteId)
            .Select(line => new DocumentLineView(
                line.Sku, line.Name, line.Quantity,
                Money(line.NetPrice), Money(line.Quantity * line.NetPrice)))
            .ToList();

        return new QuoteView(
            quote.Reference,
            quote.Status,
            quote.CreatedDate,
            quote.ExpiresDate,
            Money(quote.Value),
            quote.CustomerNote,
            lines,
            CanDecide(quote),
            WhyNot(quote));
    }

    public OrderView? Order(string reference)
    {
        if (_customer.AccountId is not { } accountId)
        {
            return null;
        }

        var siteId = _siteContext.Site.Id;
        var order = _orders.GetOrderForAccount(reference, accountId, siteId);

        if (order is null)
        {
            return null;
        }

        var lines = _orders.GetOrderLinesForAccount(order.Id, accountId, siteId)
            .Select(line => new DocumentLineView(
                line.Sku, line.Name, line.Quantity,
                Money(line.Price), Money(line.Quantity * line.Price)))
            .ToList();

        return new OrderView(
            order.Reference,
            order.Status,
            order.PurchaseDate,
            Money(order.SubTotal),
            Money(order.FinalPrice),
            order.PoNumber,
            order.FromQuoteReference,
            lines);
    }

    /// <summary>
    /// Whether the customer may accept or reject this quote.
    /// </summary>
    /// <remarks>
    /// Priced only, and not expired. Both rules live here rather than in
    /// <c>spOrder_ConvertFromQuote</c>, which lets `Requested` through on purpose: that
    /// procedure's job is to stop a double conversion, and an admin converting an unpriced
    /// quote because the customer rang up is a deliberate act. A customer accepting a price
    /// nobody has set is not.
    ///
    /// Expiry blocks rather than warns, unlike a delisted line. `ExpiresDate` is a statement
    /// the store already made in writing, and honouring it past its date is the store's choice
    /// to make, not a button's.
    /// </remarks>
    private bool CanDecide(QuoteModel quote) =>
        string.Equals(quote.Status, QuoteStatus.Priced, StringComparison.Ordinal)
        && !IsExpired(quote);

    private string? WhyNot(QuoteModel quote)
    {
        if (CanDecide(quote))
        {
            return null;
        }

        if (IsExpired(quote))
        {
            return "This quote has expired. Ask us to re-quote it and we will price it again.";
        }

        return quote.Status switch
        {
            QuoteStatus.Requested => "We are pricing this now. You will hear from us shortly.",
            QuoteStatus.Accepted => "You accepted this quote, and it is now an order.",
            QuoteStatus.Rejected => "This quote was turned down.",
            _ => "This quote is not awaiting a decision."
        };
    }

    private static bool IsExpired(QuoteModel quote) =>
        quote.ExpiresDate is { } expires && expires < DateTime.UtcNow;

    private string? Expiry(QuoteModel quote) => quote.ExpiresDate is { } expires
        ? (expires < DateTime.UtcNow ? "Expired" : $"Valid until {expires:d MMM yyyy}")
        : null;

    /// <summary>
    /// A stored amount, formatted for the store.
    /// </summary>
    /// <remarks>
    /// Through <c>CatalogPresenter.Money</c> so the currency symbol and the locale come from
    /// the same place they do everywhere else. Not through <c>ShowPrices</c>: this is the
    /// customer's own document, and a store that hides list prices from the open web still
    /// shows a customer what they were quoted.
    /// </remarks>
    private string Money(decimal amount) => _catalog.Money(amount);

    /// <summary>Records the outcome without deciding what the page says about it.</summary>
    internal void LogDecision(string reference, string what) =>
        _logger.LogInformation(
            "Quote {Reference} {Decision} by a customer at {SiteKey}.",
            reference, what, _siteContext.Site.SiteKey);
}

/// <summary>A quote or an order as a list row.</summary>
public sealed record DocumentSummary(
    string Reference, string Status, DateTime Raised, int Lines, string Value, string? Note);

/// <summary>
/// One line of a quote or an order as the customer reads it.
/// </summary>
/// <remarks>
/// No product id and no cost, for the same two reasons <c>BasketLineView</c> has neither.
/// </remarks>
public sealed record DocumentLineView(
    string Sku, string Name, int Quantity, string UnitPrice, string LineTotal);

/// <summary>The company and person a printed quote or order is addressed to.</summary>
public sealed record DocumentParty(string Company, string Contact, string Email);

public sealed record QuoteView(
    string Reference,
    string Status,
    DateTime Raised,
    DateTime? Expires,
    string Value,
    string? CustomerNote,
    IReadOnlyList<DocumentLineView> Lines,
    bool CanDecide,
    string? WhyNotDecidable);

public sealed record OrderView(
    string Reference,
    string Status,
    DateTime Placed,
    string SubTotal,
    string Total,
    string? PoNumber,
    string? FromQuoteReference,
    IReadOnlyList<DocumentLineView> Lines);
