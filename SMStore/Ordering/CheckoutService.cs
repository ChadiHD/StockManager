using System.Net.Mail;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Email;
using SMDataManager.Library.Models;
using SMDataManager.Library.Shipping;
using SMDataManager.Library.Tax;
using SMStore.Accounts;
using SMStore.Catalog;
using SMStore.Registration;
using SMStore.Sites;
using static SMStore.Localization.StoreText;

namespace SMStore.Ordering;

/// <summary>
/// Turns a cart into an order (T12), for a guest or a signed-in customer.
/// </summary>
/// <remarks>
/// <see cref="QuoteSubmissionService"/>'s rules, applied to an order. Prices are resolved here,
/// through <see cref="CatalogPresenter"/>, from the rows the cart reads back; the form carries an
/// address and never a price. Unavailable lines are dropped and named. The procedure does the
/// rest in one transaction: the stock, the guest's account, the order, its address and the cart.
///
/// Tax is assessed on the delivery country, because a consumer is taxed where the goods go. A
/// signed-in business keeps its VAT number, which is what <c>eu-b2b</c>'s reverse charge reads.
/// </remarks>
public sealed class CheckoutService
{
    public const string Path = "/checkout";

    /// <summary>A guest's order page: <c>/orders/{token}</c>.</summary>
    public const string GuestOrderPath = "/orders";

    private readonly IOrderData _orders;
    private readonly IBasketData _baskets;
    private readonly IAccountData _accounts;
    private readonly IShippingRateData _rates;
    private readonly BasketService _basket;
    private readonly CatalogPresenter _catalog;
    private readonly TaxAssessor _tax;
    private readonly IEmailOutbox _outbox;
    private readonly ISiteContext _siteContext;
    private readonly ICustomerContext _customer;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(
        IOrderData orders,
        IBasketData baskets,
        IAccountData accounts,
        IShippingRateData rates,
        BasketService basket,
        CatalogPresenter catalog,
        TaxAssessor tax,
        IEmailOutbox outbox,
        ISiteContext siteContext,
        ICustomerContext customer,
        ILogger<CheckoutService> logger)
    {
        _orders = orders;
        _baskets = baskets;
        _accounts = accounts;
        _rates = rates;
        _basket = basket;
        _catalog = catalog;
        _tax = tax;
        _outbox = outbox;
        _siteContext = siteContext;
        _customer = customer;
        _logger = logger;
    }

    public CheckoutOutcome Place(CheckoutForm form)
    {
        var contact = _customer.Contact;
        var errors = Validate(form, guest: contact is null);

        if (errors.Count > 0)
        {
            return CheckoutOutcome.Invalid(errors);
        }

        var basket = _basket.Current();

        if (basket is null)
        {
            // A second tab, or a back button after the order was placed and the cart deleted.
            return CheckoutOutcome.Empty;
        }

        var site = _siteContext.Site;
        var lines = _baskets.GetLines(basket.Id, site.Id, _customer.CustomerGroupId);
        var available = lines.Where(line => line.Available).ToList();
        var dropped = lines.Where(line => !line.Available).Select(line => line.Sku).ToList();

        if (available.Count == 0)
        {
            return CheckoutOutcome.Empty;
        }

        var country = form.Country!.Trim().ToUpperInvariant();
        var lineItems = available.Select(ToLine).ToList();

        // Delivery from the store's rates (T13), on the goods as priced. A store with rates that
        // cover nowhere the customer is going cannot deliver there, and says so on the field. A store
        // with none charges nothing, and cannot open as a checkout store.
        var rates = DeliveryRates();
        var delivery = ShippingQuote.Choose(rates, country, lineItems.Sum(line => line.Quantity * line.NetPrice));

        if (rates.Count > 0 && delivery is null)
        {
            return CheckoutOutcome.Invalid(new Dictionary<string, string>
            {
                [nameof(CheckoutForm.Country)] = L["We do not deliver to {0}.", RegistrationCountries.Name(country)]
            });
        }

        var account = _customer.AccountId is { } accountId ? _accounts.GetAccountById(accountId, site.Id) : null;
        var assessment = _tax.ForOrder(site, new AccountModel { Country = country, VatNumber = account?.VatNumber });
        var token = contact is null ? BasketToken.Mint() : null;
        var email = contact?.Email ?? form.Email!.Trim();
        var name = $"{form.FirstName?.Trim()} {form.LastName?.Trim()}".Trim();

        OrderPlacementResult result;

        try
        {
            result = _orders.PlaceFromBasket(new OrderPlacement
            {
                SiteId = site.Id,
                BasketId = basket.Id,
                ContactId = contact?.Id,
                Email = email,
                FirstName = Trimmed(form.FirstName),
                LastName = Trimmed(form.LastName),
                Phone = Trimmed(form.Phone),
                Delivery = new OrderAddressModel
                {
                    Line1 = Trimmed(form.Line1),
                    Line2 = Trimmed(form.Line2),
                    City = Trimmed(form.City),
                    Region = Trimmed(form.Region),
                    PostCode = Trimmed(form.PostCode),
                    Country = country
                },
                Lines = lineItems,
                Tax = assessment,
                GuestToken = token,
                ShippingName = delivery?.Name,
                ShippingPrice = delivery?.Price ?? 0m
            });
        }
        catch (Exception exception)
        {
            // All or nothing, so nothing was written and the cart is still there.
            _logger.LogError(exception, "Could not place an order at {SiteKey}.", site.SiteKey);

            return CheckoutOutcome.Failed;
        }

        if (result is null || !result.Placed)
        {
            return result?.ShortSkus is { Length: > 0 } shortSkus
                ? CheckoutOutcome.Short(shortSkus.Split(',', StringSplitOptions.RemoveEmptyEntries))
                : CheckoutOutcome.Failed;
        }

        _logger.LogInformation("Order {Reference} placed from the cart at {SiteKey}, {Kind}.",
            result.Reference, site.SiteKey, contact is null ? "guest" : "signed in");

        if (contact is null)
        {
            // The cart it named is gone. Left, the cookie would name a basket nobody has.
            _basket.Forget();
        }

        Acknowledge(result.Reference, token, email, name);

        return CheckoutOutcome.Placed(result.Reference, token, dropped);
    }

    /// <summary>The store's active delivery rates, which the checkout page lists and the checkout charges from.</summary>
    public IReadOnlyList<ShippingRateModel> DeliveryRates() =>
        _deliveryRates ??= _rates.GetBySite(_siteContext.Site.Id).Where(rate => rate.IsActive).ToList();

    private IReadOnlyList<ShippingRateModel>? _deliveryRates;

    /// <summary>
    /// Queues <c>order.placed</c>, after the order is written.
    /// </summary>
    /// <remarks>
    /// Here rather than in the procedure, because a guest's link is a credential and only
    /// <see cref="IEmailOutbox"/> encrypts a payload. That is the one-insert window registration
    /// and password reset accept, and like theirs a failure is logged rather than shown: the order
    /// exists, and failing the page would have the customer place it twice.
    /// </remarks>
    private void Acknowledge(string reference, string? token, string email, string name)
    {
        var siteId = _siteContext.Site.Id;

        try
        {
            var order = token is not null
                ? _orders.GetOrderByGuestToken(token, siteId)
                : _orders.GetOrderForAccount(reference, _customer.AccountId!.Value, siteId);

            _outbox.Enqueue(siteId, email, name, EmailTemplates.OrderPlaced, new OrderPlacedPayload(
                order.Reference, order.SubTotal, order.VAT, order.FinalPrice,
                order.TaxTreatment, order.TaxLegend, token));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not queue the confirmation for order {Reference} at {SiteKey}.",
                reference, _siteContext.Site.SiteKey);
        }
    }

    private QuoteRequestLine ToLine(BasketLineModel line)
    {
        var resolved = _catalog.Resolve(line.RetailPrice, line.Cost);

        return new QuoteRequestLine
        {
            ProductId = line.ProductId,
            Quantity = line.Quantity,
            ListPrice = resolved.ListPrice,
            DiscountPct = resolved.EffectiveDiscountPct,
            NetPrice = resolved.NetPrice
        };
    }

    /// <summary>
    /// Everything wrong with the form, by field. The server's check, and the only one that counts.
    /// </summary>
    /// <remarks>
    /// The lengths are the columns', so a value that would not fit is refused here rather than
    /// truncated or thrown by the procedure. The country must be one the form offered, because
    /// the tax is assessed on it.
    /// </remarks>
    public static Dictionary<string, string> Validate(CheckoutForm form, bool guest)
    {
        var errors = new Dictionary<string, string>();

        Require(nameof(CheckoutForm.FirstName), form.FirstName, 100, L["First name"]);
        Require(nameof(CheckoutForm.LastName), form.LastName, 100, L["Last name"]);
        Require(nameof(CheckoutForm.Line1), form.Line1, 200, L["Address"]);
        Require(nameof(CheckoutForm.City), form.City, 100, L["City"]);
        Limit(nameof(CheckoutForm.Line2), form.Line2, 200);
        Limit(nameof(CheckoutForm.Region), form.Region, 100);
        Limit(nameof(CheckoutForm.PostCode), form.PostCode, 20);
        Limit(nameof(CheckoutForm.Phone), form.Phone, 50);

        if (string.IsNullOrWhiteSpace(form.Country)
            || !RegistrationCountries.All.ContainsKey(form.Country.Trim().ToUpperInvariant()))
        {
            errors[nameof(CheckoutForm.Country)] = L["Choose the country we are delivering to."];
        }

        if (guest)
        {
            var email = form.Email?.Trim();

            if (string.IsNullOrEmpty(email) || email.Length > 256
                || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            {
                errors[nameof(CheckoutForm.Email)] = L["Enter the email address we should send your order to."];
            }
        }

        return errors;

        void Require(string field, string? value, int max, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors[field] = L["{0} is required.", label];
            }
            else
            {
                Limit(field, value, max);
            }
        }

        void Limit(string field, string? value, int max)
        {
            if (value is not null && value.Trim().Length > max)
            {
                errors[field] = L["That is longer than we can store ({0} characters at most).", max];
            }
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>What the checkout form posted. Every value is the customer's, so none is trusted.</summary>
public sealed record CheckoutForm(
    string? Email,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostCode,
    string? Country);

/// <summary>
/// What came of a checkout. Shaped like <see cref="QuoteSubmission"/>: each outcome needs its own
/// words, and only one is a fault.
/// </summary>
public sealed class CheckoutOutcome
{
    private CheckoutOutcome(
        string? reference, string? guestToken, IReadOnlyDictionary<string, string> errors,
        IReadOnlyList<string> shortSkus, IReadOnlyList<string> dropped, bool empty)
    {
        Reference = reference;
        GuestToken = guestToken;
        Errors = errors;
        ShortSkus = shortSkus;
        DroppedSkus = dropped;
        IsEmpty = empty;
    }

    public string? Reference { get; }

    /// <summary>A guest's way back to the order, or null for a signed-in customer.</summary>
    public string? GuestToken { get; }

    public IReadOnlyDictionary<string, string> Errors { get; }

    /// <summary>What the store holds too few of. Nothing was written.</summary>
    public IReadOnlyList<string> ShortSkus { get; }

    /// <summary>Lines left out because the store can no longer supply them.</summary>
    public IReadOnlyList<string> DroppedSkus { get; }

    /// <summary>No cart, or nothing in it that can be supplied.</summary>
    public bool IsEmpty { get; }

    public bool Succeeded => Reference is not null;

    private static readonly IReadOnlyDictionary<string, string> NoErrors = new Dictionary<string, string>();

    public static readonly CheckoutOutcome Empty = new(null, null, NoErrors, [], [], true);

    public static readonly CheckoutOutcome Failed = new(null, null, NoErrors, [], [], false);

    public static CheckoutOutcome Invalid(IReadOnlyDictionary<string, string> errors) =>
        new(null, null, errors, [], [], false);

    public static CheckoutOutcome Short(IReadOnlyList<string> skus) => new(null, null, NoErrors, skus, [], false);

    public static CheckoutOutcome Placed(string reference, string? token, IReadOnlyList<string> dropped) =>
        new(reference, token, NoErrors, [], dropped, false);
}
