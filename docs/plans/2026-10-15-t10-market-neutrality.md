# T10 — Market neutrality: prices with tax in, and a store in any language

Detail plan for the tenth template phase, the first in a B2C track added on 2026-10-15. The
requirement behind it: the template must run **consumer shops** — a cart, a card payment,
guests — as well as the trade stores T0–T9 built. The first of them is a clothing store
launching in Brazil, and nothing shared may assume Brazil, because the next one could be
anywhere.

**Status: built** on `t10-market-neutrality`, in three commits:
1. the tax basis, the `flat` rule set and the money symbol;
2. the labels that state the basis;
3. the culture and the localiser.

## 1. The B2C track

The platform phases run before the first consumer tenant, as T0–T9 ran before aclitrade.ie:

| Phase | Scope |
| --- | --- |
| **T10** | Prices that include tax, a tax rule set for a shop charging one rate, money in any currency's own symbol, the culture pinned to the store, and the storefront's words behind a localiser |
| T11 | Apparel catalogue: variants (size and colour) as `Product` rows under a parent, option facets, uploaded images, stock reserved by an order |
| T12 | The `DirectCheckout` ordering mode, a consumer account with no approval and no paperwork, guest buyers, and an order placed from a basket rather than a quote, with its addresses snapshotted |
| T13 | Payment through Stripe Checkout (hosted page, webhook, per-store credentials), flat shipping rates, fulfilment and its mail, and an open checklist that knows about all of it |
| T14 | A fiscal-document seam, with an outbox and a logging issuer, for Brazil's NF-e and anything like it |

**Decided with the owner on 2026-10-15:**
- Stripe Checkout first, the hosted page. Card data never reaches this application (storefront
  plan, rule 8).
- Guest checkout, with an optional account.
- Own stock entered by an admin, not a feed.
- The template ships **English only** for now. Localisation is a mechanism here, and a
  translation is a file added later.
- **Email wording is per store**, through the `/admin/email` screen T9 built. A Portuguese store
  writes its own messages there, and the platform's wording stays English.

## 2. What was wrong for a consumer shop

Reading the code found these gaps:

1. **Every price is net.** `Product.RetailPrice` is before tax. `spOrder_ConvertFromQuote` adds
   tax on top, and every label says "excl. tax". A consumer sees, and is charged, the price
   with tax in. Displaying `net × (1 + rate)` instead would break on charm prices: a stored net
   price rounded to the cent cannot reproduce every gross price.
2. **`Product.RetailPrice` is not per store.** Own stock may be sold by every store in its
   currency (`fnSite_ProductSellable`), and the desktop till reads the same column as net. So a
   flag on the store alone would make one number gross on one store and net on another.
3. **Both rule sets are trade rules.** `eu-b2b` and `uk-b2b` decide reverse charge and export
   from a VAT number and a country. A Brazilian shop under `eu-b2b` is a category error.
4. **`SiteMoney` knows three symbols.** EUR, GBP and USD. Every other currency printed its
   code: "BRL 99,90", not "R$ 99,90".
5. **Nothing sets the culture.** Dates in `"d MMM yyyy"` take the server's culture, which is
   whatever the container's `LANG` says — `Site.Locale` reaches only `<html lang>`, `SiteMoney`
   and the content lookup.
6. **About 340 distinct customer-facing strings are English literals**, across 30 Razor files
   and about 85 in C#. Some markup also builds phrases that no translation survives: a mode's
   label lowercased in sixteen places, and pluralised with an appended "s" in four.

## 3. Decisions

### 3.1 A price is stated in its store's terms

**`Site.PricesIncludeTax`**, and **`Product.PricesIncludeTax`** beside `Product.CurrencyCode`:

- **The product carries its basis, the way it carries its currency.** Its price is a number in
  a currency *and* on a basis, and a store sells only products priced in its own terms. So
  `fnSite_ProductSellable` adds the basis to the currency test. A product created by an admin
  takes the acting store's basis, as it takes its currency.
- **Feed products are net**, because a distributor's price is a trade price. A store whose
  prices include tax therefore sells own stock only.
- **The till lists net products only** (`spProduct_GetAll`), because it adds tax on top.
- **The basis is locked when the currency is**, by `fnSite_CurrencyLocked`, with the same
  refusal and for the same reason: every quote, account and feed price the store holds is a
  number on the current basis. That lock is also why an order needs no snapshot of its basis. A
  store with an order has a quote, so its basis can no longer change.
- **No margin floor on a store whose prices include tax.** `Cost` is net, and the floor
  compares it with the price. Making the floor gross means carrying the rate and
  `IsTaxable` into `PriceResolver` and its SQL copy, for a feature consumer stores do not use
  (it binds only under group discounts). `SiteSettingsRules` refuses the combination instead.

**The order backs the tax out, line by line.** On an inclusive store `spOrder_ConvertFromQuote`
computes, for each taxable line:

  `VAT = ROUND(qty × price × rate / (100 + rate), 2)`

and then `SubTotal = Σ(qty × price) − Σ VAT`, and `FinalPrice = Σ(qty × price)`. The total is
therefore exactly the sum of the prices the customer was shown, which is the whole point. Taxing
the net figure instead would drift a cent from the displayed total at about ten units. The
existing formula is unchanged on every other store.

- **A zero rate on an inclusive store charges the shown price, with no tax in it.** That
  happens under reverse charge, an export, or an exempt product. Stripping the tax back out for
  an export is a cross-border decision, and it belongs with destination tax (§5).

### 3.2 `flat`: one rate on every taxable sale

A third rule set. It charges the store's standard rate wherever the buyer is, and charges
nothing on a product marked exempt. That covers a shop selling domestically, which is every
consumer store this template has been asked for: Brazil, Ireland, the UK, Australia, and an EU
shop under the distance-selling threshold.

Like `eu-b2b` and `uk-b2b`, it is an engine and not tax advice. An accountant signs it off for
each store before launch.

### 3.3 Money in its own currency's symbol

`SiteMoney` prefers the symbol of the store's locale **when that locale's own currency is the
store's**, so pt-BR and BRL gives "R$". Otherwise the existing table applies. That table matters
because an en-IE store trading in GBP still wants "£", not en-IE's "€".

### 3.4 The culture is the store's

`SiteResolutionMiddleware` sets `CurrentCulture` and `CurrentUICulture` from `Site.Locale` once
the store is known.
- **Not from `Accept-Language`.** The store decides its language as it decides its currency,
  and its content rows are written in that language.
- **Not `UseRequestLocalization`.** That needs a list of supported cultures, and here that list
  is the `Site` table.

What this changes today:
- Month names in dates follow the store.
- `N0` and `0.##` follow it too.
- Nothing that is parsed changes: query parameters bind as strings, and the form binder and the
  route constraints are invariant.
- `SiteMoney` already used the store's culture.

### 3.5 The storefront's words go through `StoreText.L`

- **The English text is the key.** No `.resx` exists until a translation is added, and a
  missing key renders the English. That is the framework's own fallback, so an English store
  needs no file, and a page's markup still reads as its words.
- **One static `IStringLocalizer`, `StoreText.L`.** Razor writes `L["…"]` through
  `@using static` in `_Imports.razor`, and C# uses the same `using static`. It is not injected:
  a lookup over embedded resources has no per-request state and nothing to substitute, and
  injecting it would have made every component, and every bUnit test context, register a
  service, for no behaviour.
- **A phrase is a key, never assembled from pieces.** Lowercasing a label, or adding "s" to
  it, goes. The words become whole keys with placeholders, because word order and plurals are
  per language.
  - **A sentence that names the basket** takes the mode's new `IOrderingMode.BasketNoun`
    ("quote", "cart") as a placeholder, in place of `BasketLabel` lowercased.
  - **This is the one deliberate exception, and why it is safe for now.** A language whose
    nouns have gender, Portuguese among them ("sua cotação", "seu carrinho"), cannot write
    "Your {0}" for both. When a gendered translation arrives with a second mode, those
    phrases move onto the mode, which is T12's to shape.
  - **The quote pages name quotes, plurals included.** That is what they list, so the words are
    literal there.
- **`StoreTextKeysTests`** scans the storefront for `L["…"]`.
  - It fails when any translation (`SMStore/Localization/StoreText.{culture}.resx`) lacks one of
    those keys, or carries a key nobody uses.
  - It fails on a key built at run time.
  - It fails when the scan finds suspiciously few keys.
  - With no translation yet, the translation check passes vacuously. It is in place so the
    first `StoreText.pt-BR.resx` is held to the whole storefront.
- **What the conversion left for the first translation**, all of it English-safe today:
  - **The catalog's count** chooses between "product" and "products" by count. A language with
    more plural forms needs a plural-aware key.
  - **Splits around markup.** A few sentences wrap a link or a `<strong>` and are split around
    it: "from" before a quote link on the order page, and the sign-in and registration
    prompts. Each piece is a key, but a translator sees fragments.
  - **The development error page** is also split around markup. It is ASP.NET's boilerplate,
    and no customer sees it.
- **A form decides which field an error belongs to from the error, not its wording.**
  `Register.razor` found the password's errors with `Message.Contains("password")`, which no
  translation survives.
- **Out of this phase, and why:**
  - **Tax legends and treatments** are snapshotted onto an order as a legal statement
    (`Purchase.TaxLegend`). Which language that statement takes is the rule set's decision, and
    it is made with the first translation.
  - **Status values shown as stored** ("Awaiting payment") go the same way.
  - **Identity's own error descriptions** need an `IdentityErrorDescriber`.
  - **The country list** sorts by `RegionInfo.EnglishName`.
  - **The admin portal stays English.** It is staff's tool, not the shop.

## 4. Exit

- An inclusive store shows a product at the price an admin typed, labelled as including tax.
  An order of ten at that price totals exactly ten times it, with the tax backed out per line.
- An exclusive store, a feed and the till behave exactly as before.
- Own stock created on an inclusive store does not appear on an exclusive store in the same
  currency, or on the till.
- `flat` is offered on the settings screen, and charges the store rate to a customer in any
  country.
- A pt-BR, BRL store shows "R$ 1.234,56" and Portuguese month names.
- Every customer-facing string in SMStore goes through the localiser. Rendered English is
  unchanged, and every existing test passes untouched.

## 5. Not in T10

- **Destination-based consumer tax**, meaning EU OSS above the threshold or US sales tax. It
  needs a delivery address, which T12 brings, and probably a provider. The next rule set,
  perhaps Stripe Tax.
- **Address and identity fields at checkout** (a CPF, a CEP). These belong with the checkout
  that asks for them, in T12.
- **`quote.priced` says "before tax"** in the platform's wording. A quote-taking store whose
  prices include tax overrides that wording at `/admin/email`.
