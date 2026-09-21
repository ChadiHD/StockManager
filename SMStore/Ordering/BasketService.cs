using SMDataManager.Library.DataAccess;
using SMStore.Catalog;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Sites;

namespace SMStore.Ordering;

/// <summary>
/// The storefront's basket: finds it, changes it, and hands it over at sign-in.
/// </summary>
/// <remarks>
/// Everything about *which* basket belongs to this request lives here, so no page or endpoint
/// has to reason about cookies, tokens or the signed-in contact. The rules it holds:
///
/// - A signed-in contact's basket is found by who they are. An anonymous visitor's is found by
///   the token in their cookie, and only while that basket has not been claimed.
/// - Reading never creates. A basket row exists once something has been added to it, so a
///   crawler walking the catalog leaves nothing behind.
/// - The site comes from <see cref="ISiteContext"/> and the contact from
///   <see cref="ICustomerContext"/>, never from a form field. A basket is a scoped entity like
///   any other, and both procedures take the site as a predicate rather than as a hint.
/// </remarks>
public sealed class BasketService
{
    private readonly IBasketData _baskets;
    private readonly CatalogPresenter _catalog;
    private readonly ISiteContext _siteContext;
    private readonly ICustomerContext _customer;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<BasketService> _logger;

    public BasketService(
        IBasketData baskets,
        CatalogPresenter catalog,
        ISiteContext siteContext,
        ICustomerContext customer,
        IHttpContextAccessor http,
        ILogger<BasketService> logger)
    {
        _baskets = baskets;
        _catalog = catalog;
        _siteContext = siteContext;
        _customer = customer;
        _http = http;
        _logger = logger;
    }

    /// <summary>The lines in this request's basket, empty when there is no basket at all.</summary>
    public IReadOnlyList<BasketLineModel> Lines()
    {
        var basket = Current();

        return basket is null
            ? []
            : _baskets.GetLines(basket.Id, _siteContext.Site.Id, _customer.CustomerGroupId);
    }

    /// <summary>
    /// How many distinct products are in the basket, for the header's badge.
    /// </summary>
    /// <remarks>
    /// Distinct lines rather than total units, because "3" beside a basket icon meaning "one
    /// product, three of them" reads as three products. Rendered on every page, so it is one
    /// read of the lines the page may also be about to render — <see cref="Lines"/> is called
    /// separately on the basket page and the duplication is one indexed query.
    /// </remarks>
    public int LineCount() => Lines().Count;

    /// <summary>
    /// Adds a product by SKU, or raises the quantity of the line already there. Creates the
    /// basket and its cookie if this is the first thing added.
    /// </summary>
    /// <remarks>
    /// By SKU rather than by product id, so no database id ever appears in a storefront form.
    /// Resolving it is also the first of two visibility checks: <c>spCatalog_GetBySku</c>
    /// answers for this site and this customer group, and <c>spBasket_AddLine</c> asks the
    /// same question again through the same function. A form post says nothing about the page
    /// it came from, so one check would be the minimum and two cost one indexed read on a
    /// path nobody clicks in a loop.
    /// </remarks>
    public bool Add(string? sku, int quantity)
    {
        var site = _siteContext.Site;

        if (string.IsNullOrWhiteSpace(sku))
        {
            return false;
        }

        var product = _catalog.GetBySku(sku.Trim());

        if (product is null)
        {
            // Not in this store, or hidden from this group, or it went stale or delisted
            // between the page rendering and the button being pressed. One answer for all
            // three, because the customer can do the same thing about each.
            return false;
        }

        var basket = _baskets.EnsureBasket(site.Id, EnsureToken(), _customer.Contact?.Id);

        if (basket is null)
        {
            // The procedure creates a row or returns the one that already exists, so this is
            // not a case that should arise. Logged rather than thrown because the customer's
            // next click is "try again" either way.
            _logger.LogError("No basket could be resolved at {SiteKey}.", site.SiteKey);

            return false;
        }

        try
        {
            _baskets.AddLine(basket.Id, site.Id, product.Id, quantity, _customer.CustomerGroupId);

            return true;
        }
        catch (Exception exception)
        {
            // The expected failure is 50021: a product that is not in this store's catalog, or
            // is hidden from this customer's group. It reaches the customer as "that product is
            // no longer available" rather than as an error page, because by the time they
            // clicked it may simply have gone stale.
            _logger.LogWarning(exception,
                "Refused to add {Sku} to a basket at {SiteKey}.",
                sku, site.SiteKey);

            return false;
        }
    }

    /// <summary>Sets a line quantity, or removes the line when it is below one.</summary>
    /// <remarks>
    /// Removal is a quantity of zero. See <see cref="IBasketData.SetQuantity"/>.
    ///
    /// By SKU, like <see cref="Add"/>, but resolved against the basket rather than against
    /// the catalog: a line whose product has since been delisted must still be removable, and
    /// <c>spCatalog_GetBySku</c> would no longer return it.
    /// </remarks>
    public bool SetQuantity(string? sku, int quantity)
    {
        var basket = Current();

        if (basket is null || string.IsNullOrWhiteSpace(sku))
        {
            return false;
        }

        var siteId = _siteContext.Site.Id;

        var line = _baskets.GetLines(basket.Id, siteId, _customer.CustomerGroupId)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Sku, sku.Trim(), StringComparison.OrdinalIgnoreCase));

        return line is not null
            && _baskets.SetQuantity(basket.Id, siteId, line.ProductId, quantity);
    }

    /// <summary>
    /// Hands the browser's basket to a contact who has just signed in.
    /// </summary>
    /// <remarks>
    /// Called from the sign-in endpoint, with the contact passed in: the cookie has only just
    /// been written, so <see cref="ICustomerContext"/> is still empty on this request —
    /// <c>CustomerSessionValidator</c> fills it from the next one onwards.
    ///
    /// Never fails the sign-in. Somebody who has just proved who they are must get in whether
    /// or not their basket came with them, and an exception here would send them back to the
    /// login page with the same indistinguishable failure a wrong password produces.
    /// </remarks>
    public void ClaimFor(int contactId)
    {
        var token = ReadToken();

        if (token is null)
        {
            return;
        }

        try
        {
            _baskets.ClaimBasket(_siteContext.Site.Id, token, contactId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Could not attach a basket to contact {ContactId} at {SiteKey}.",
                contactId, _siteContext.Site.SiteKey);
        }
    }

    /// <summary>
    /// Drops the basket cookie, called on sign-out.
    /// </summary>
    /// <remarks>
    /// A claimed basket keeps its token, so the cookie left behind names a basket that now
    /// belongs to a customer. <c>spBasket_Find</c> refuses to return a claimed basket to an
    /// anonymous caller, which is the half that holds without a response header arriving; this
    /// is the half that stops the browser presenting a stale credential at all.
    /// </remarks>
    public void Forget() => _http.HttpContext?.Response.Cookies.Delete(BasketToken.CookieName);

    /// <summary>This request's basket, without a round trip when there cannot be one.</summary>
    /// <remarks>
    /// The header counts the basket on every page, so the common case - a first-time visitor
    /// with no cookie and no session - must not cost a query. Neither lookup could match.
    /// </remarks>
    public BasketModel? Current()
    {
        var token = ReadToken();
        var contactId = _customer.Contact?.Id;

        if (token is null && contactId is null)
        {
            return null;
        }

        return _baskets.FindBasket(_siteContext.Site.Id, token, contactId);
    }

    /// <summary>The token in the cookie, or null when there is none worth looking up.</summary>
    private string? ReadToken()
    {
        var value = _http.HttpContext?.Request.Cookies[BasketToken.CookieName];

        return BasketToken.IsWellFormed(value) ? value : null;
    }

    /// <summary>The token in the cookie, minting and setting one when there is none.</summary>
    private string EnsureToken()
    {
        var existing = ReadToken();

        if (existing is not null)
        {
            return existing;
        }

        var token = BasketToken.Mint();

        _http.HttpContext?.Response.Cookies.Append(BasketToken.CookieName, token, new CookieOptions
        {
            // Nothing in the browser needs to read this, and a script that could read it could
            // take the basket.
            HttpOnly = true,
            Secure = true,
            // Lax rather than Strict: a customer following a link from a quote email or a
            // search result arrives on a cross-site navigation, and under Strict the cookie
            // would be withheld on exactly that request — so their basket would look empty on
            // the first page they land on and appear again when they clicked something.
            SameSite = SameSiteMode.Lax,
            // Not subject to consent: without it the basket does not work at all, which is
            // what "strictly necessary" means. There is no analytics value in it.
            IsEssential = true,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.Add(BasketToken.Lifetime)
        });

        return token;
    }
}
