# T9 — Store administration and a second country

Detail plan for the ninth template phase, added to
`docs/plans/2026-09-10-storefront-implementation-plan.md` on 2026-10-06 from four requirements:

1. **aclitrade.ie and aclitech.co.uk launch together**, from one deployment, managed in one admin
   portal whose header selector chooses the store an admin is acting for.
2. **aclitech.co.uk sells from a UK distributor, priced in GBP.** aclitrade.ie keeps its Irish
   feed in EUR.
3. **Everything a store is configured with by SQL today gets an admin screen.**
4. **An admin can be limited to particular stores.**

It also takes the schema-deploy cleanup T7 found. T0–T8 are merged.

**This changes v1's scope.** The implementation plan listed "a second live store" under *Out of
v1*. It is in now, so the tenant track runs twice after T9: A0–A4 for aclitrade.ie and B0–B4
for aclitech.co.uk (implementation plan §5).

**Exit:**

- **Store-limited admins.** An admin limited to one store sees only that store in the selector.
  The API answers a forged `X-Site-Key` for any other store exactly as it answers an unknown
  key. Revoking access takes effect on that admin's next request.
- **A new store with no SQL and no shared-code change.** It is created inactive, then
  configured — settings, categories, content pages, home page, email wording — and activated,
  all in the portal. The only files it needs are its theme, in the downstream repo (item 5).
- **Two currencies on one database.**
  - The GBP store shows only its own feed's products, in pounds; the EUR store shows only its
    own, in euros.
  - Quotes and orders on each carry their store's currency.
  - Neither store's category screen lists the other's feed categories.
- **Feed renames.** Renaming a feed keeps its products updating and delisting.
- **UK tax.** A UK customer of aclitech is charged 20%, and an Irish customer of aclitech is
  zero-rated as an export. Each order carries its legend. T6's journey still passes unchanged on
  the Irish store.
- **UK registration.** aclitech's registration form asks for a Companies House number and
  accepts a GB VAT number.
- **Legal identity.** Order documents and the footer carry the store's legal name, company
  number, registered office and VAT number.
- **Idle publishes.** Publishing the DACPAC to a database already at the current schema performs
  no operations, and CI asserts it.

**Status: items 1–3 built**, on `t9-store-administration`, a commit per sub-item. Built in three pull requests: items
1–2, item 3, then items 4–5.

---

## 1. What is already wrong

As in every phase since T5, reading the path before building on it changed the shape of the
work.

### 1a. A price does not know its currency

`Product.RetailPrice` and `Product.Cost` are bare `money`, on a table every store shares.
`Site.CurrencyCode` only chooses the symbol: `SiteMoney.Format` would print `£` in front of the
Irish feed's euro figure. Around that:

- **Admin screens choose a document's currency without reference to the store.**
  - New account: `Accounts.razor:82` offers EUR/GBP/USD, and `spAccount_Insert` stores the
    choice.
  - New quote: `Quotes.razor:71`. The value is pre-filled from the account, but editable.
  - New manual order: `Orders.razor:71`.
  - Only self-registration and the storefront's quote request derive the currency from the
    store.
- **The portal writes "EUR" literally.**
  - `Products.razor` and `ProductDetail.razor` write the string.
  - `Dashboard.razor:127` sums EUR orders only.
  - `spReport_GetSales` falls back to `'EUR'`.

### 1b. A store sells whatever its category names match, from any distributor

`fnSite_ProductPlacement` joins `CategoryMapping.FeedValue` to `Product.Category`. Nothing in it
refers to where the product came from. Once a UK feed and an Irish feed share one product table,
a UK admin who maps "Notebooks" sells the Irish distributor's notebooks at euro prices — stock
the UK store has no account to buy.

The Categories screen shows the same leak. `spCategoryMapping_GetForSite` and
`spCategoryMapping_GetUnmapped` list distinct `Product.Category` values over every product in
the database, not just the acting store's.

### 1c. A product's distributor is its feed's display name

`spProduct_BulkUpsertFromFeed` merges `ON target.Distributor = @Distributor AND
target.DistributorSku = source.DistributorSku`, and `@Distributor` is the feed's `Name`
(`DistributorFeedSyncService.cs:183`). Delisting is scoped by the same string. Two consequences:

- **Renaming a feed orphans every product it brought** (`spDistributorFeed_Update.sql:30`). The
  next sync inserts a fresh copy of the catalog under the new name. The old rows are never
  updated or delisted again, so they stay on sale at their last price indefinitely.
- **Two stores whose feeds share a name share one product set.** `UQ_DistributorFeed_Name` is
  `(SiteId, Name)`, so this is allowed. Each sync then delists the other's products.

No test runs that MERGE or the delisting SQL against a database.

### 1d. An unmapped feed field quietly reads FlexIT's column

`DistributorFeedModel.ToSettings()` (:66-95) replaces a blank field mapping with FlexIT's
element name. A second distributor's feed that leaves a field blank reads whatever its file
happens to have under FlexIT's name — or nothing — and no error is raised.

### 1e. There is one tax rule set, and it is the EU's

`eu-b2b` zero-rates anyone outside the EU as an export, and `TaxRuleSetTests.cs:75` asserts that
for GB. No UK rule set exists. Both hosts register rule sets separately
(`SMStore/Program.cs:98`, `StockApi/Program.cs:80`). A rule set added to one host only would
render on the storefront and then throw when an admin converts the quote.

### 1f. The EU registration form refuses every British business

`eu-b2b` makes the VAT number required. Its `VatPrefixes` (`EuB2bRegistrationFieldSet.cs:64-69`)
are the EU-27 plus `XI`, without `GB`, so a GB number is refused as "not an EU VAT country
code". **No GB business can open an account on aclitrade.ie today.** The labels are Irish:
"CRO", "County", "Eircode or postcode".

### 1g. The store's own legal identity is printed nowhere

`Site.TaxRegistrationNumber` is read by nothing beyond `SiteModel`, although `Site.sql`'s own
comment says a customer cannot reclaim VAT against a document that lacks it. The schema has no
legal name, company number or registered office at all. Irish and UK company law both expect
them on a trading website and its business documents (solicitor to confirm the exact list).
Neither the footer nor `DocumentSheet` shows them.

### 1h. The home page is a placeholder

`Home.razor` renders the store's country and currency as a kicker, then the text "Storefront
chrome and the design system are in place. The hero copy, category grid and featured products
arrive with the content mechanism and the catalog", then a stub that says "land with T2". There
is no `home` content key; `ContentPage` serves solutions, about, contact, terms, privacy and
cookies. A1's "Home content from the artboards" has nowhere to go.

### 1i. Store configuration has read procedures only

- **No write procedures.** `Site`, `SiteCategory`, `SiteContent` and `SiteEmailTemplate` have
  three, one, one and one procedures respectively, all reads.
- **Categories nobody can create.** The Categories screen maps feed values onto store categories
  that nobody can create; the E2E suite inserts them by SQL.
- **`PriceDisplay` is validated nowhere.** `CatalogPresenter.cs:48` compares the value against
  `"Authenticated"`, so a typo behaves as `Public`. That is the one failure that shows trade
  prices to everyone.
- **The other keys fail only at run time.** `OrderMode`, `RegistrationFieldSet` and `TaxRuleSet`
  are checked only by their providers throwing, so the first request after a typo is a 500.

### 1j. An inactive store cannot be administered

`spSite_GetByKey` filters `IsActive = 1`, and the admin middleware resolves through it. A new
store could therefore only be configured after it was already live.

### 1k. Admins are global, and some admin endpoints are global by accident

- **No user check.** `AdminSiteResolutionMiddleware` checks no user. `AdminSiteContext.cs:15-17`
  already names the fix: "may this user select this site".
- **The Users screen lists every customer of every store.** `api/User/Admin/GetAllUsers`
  (`UserController.cs:162-189`) returns `_context.Users` — every Identity login, customers of
  every store included.
- **A role can go to the wrong login.** `UpdateUserRoles` looks its target up by email with
  `FirstOrDefault` (`AdminDataService.cs:1048-1080`), so when a customer login shares a staff
  member's address, the customer login can receive the role while the staff member does not.
  That is a broken grant, not an escalation: `StaffSignIn` refuses site-qualified names at
  `/token`. It is still wrong.
- **Product edits reach every store.** `ProductController`'s `Create`, `Update`, `GetBySku` and
  `EnrichImages` act on the shared `dbo.Product` with no store. `spProduct_Update` writes
  `RetailPrice`, so an admin acting for one store reprices every store selling that row.
- **The selected store outlives the session.** The portal's `adminSiteKey` survives sign-out
  and the next user's sign-in (`AuthStateProvider.cs:83`). `SwitchSiteAsync` does not check the
  key against the list it was offered.

### 1l. Nine check constraints are rebuilt on every publish

Every publish drops and re-creates the following, and each re-creation re-validates every row
of its table:

- `CK_AccountDocument_Kind`, `CK_AccountDocument_Status`
- `CK_Address_Kind`
- `CK_Contact_RoleInAccount`, `CK_Contact_Status`
- `CK_EmailOutbox_Status`
- `CK_Purchase_Status`
- `CK_Quote_Status`
- `CK_SiteProduct_Visibility`

The cost grows with the data. As far as read, the cause is that SQL Server stores
`[Kind] IN ('Billing', 'Shipping')` as `([Kind]='Shipping' OR [Kind]='Billing')`
(`sys.check_constraints` on the development database), so the stored definition never matches
the source. All nine use `IN`, and `CK_Purchase_Placer`, written with `CASE`, does not drift.
Item 1 proves the cause with a deploy report before and after.

---

## 2. Decisions

### 2a. Store access is a row checked per request, not a claim in the token

- **The schema.** `dbo.UserSite (UserId, SiteId)` lists the stores a staff user may act for.
  `dbo.[User].AllSites` marks the users who may act for all of them.
- **The check.** `AdminSiteResolutionMiddleware` checks access after resolving the header,
  through one primary-key read per API request, with no cache.
- **Why not a JWT claim.** A token lives a day. Revoking someone's access to a store has to
  apply to their next request, not tomorrow's.
- **What a refusal looks like.** A store the user may not act for is left unresolved, exactly
  like an unknown key, so the API answers with the same 400. "Not yours" and "not here" are one
  answer, as they are for documents.

| | Store-limited admin | All-stores admin |
| --- | --- | --- |
| Settings, categories, content and email wording of their stores | yes | yes |
| A store's `Domain`, activation, and creating a store | no | yes |
| Users and their store grants | no | yes |
| POS endpoints (`Purchase` report, `Inventory`), which have no store | no | yes |
| Products | only those their store can sell (item 4a) | the same rule, per acting store |

Upgrading must not lock today's admins out. If no user holds `AllSites`, `Seed.sql` gives it to
every existing user, once — after that someone holds it and the step does nothing.
`AdminBootstrap` sets it on the first admin. A procedure refuses to remove `AllSites` from the
last user who has it.

### 2b. A product belongs to the feed that brought it, and its price carries a currency

- **`Product.FeedId`** names the feed. It replaces the name as the merge and delist key, which
  fixes 1c.
- **`Product.CurrencyCode`** is the currency of `RetailPrice` and `Cost`, taken from the feed's
  store when a product is imported and from the acting store when an admin creates one.
- **The placement rule** goes in one inline function. A product is sellable on a store only when
  its currency is the store's and, for a distributor product, its feed belongs to that store:

  ```
  p.CurrencyCode = site.CurrencyCode
  AND (   (p.Source = 'Distributor' AND feed.SiteId = site.Id)
       OR (ISNULL(p.Source, 'Own') <> 'Distributor'))
  ```

  A distributor product with no feed — an orphan from 1c, or from a deleted feed — is sellable
  nowhere.

Rejected alternatives:

- **Exchange rates or per-store price lists.** Multi-currency within one store stays out of v1,
  and aclitech has its own GBP feed, so nothing needs converting.
- **The currency test alone.** It would let a second EUR store sell a distributor's stock it
  has no account with. The feed test alone would leave own stock unpriced in the wrong currency.

### 2c. A document's currency is its store's

`spAccount_Insert`, `spQuote_Insert` and `spOrder_Insert` take the currency from the store and
stop accepting it from the caller, and the portal's currency selects go. Existing rows whose
currency differs from their store's are reported by the migration and not rewritten — they are
history.

### 2d. Setting keys come from one list both hosts check

`SiteSettingKeys` in `SMDataManager.Library` lists the valid `OrderMode`,
`RegistrationFieldSet`, `TaxRuleSet` and `PriceDisplay` values. The settings screen offers only
those, and the procedure refuses anything else.

The implementations behind the keys live in two hosts: registration field sets and ordering
modes in `SMStore`, tax rule sets in the library. Two tests in `SMStore.Tests` keep the list
honest:

- every registered implementation's key is in the list, and every listed key has an
  implementation;
- both hosts register the same tax rule sets, through one `AddTaxRuleSets()` extension instead
  of two hand-kept lines.

`PriceDisplay` also gets `CK_Site_PriceDisplay`, written in the stored form from item 1.

### 2e. Store content is sanitized, because an editor makes it writable by more people

`SiteContent.BodyHtml` renders as `MarkupString`, and until now only someone with database
access could write it. With an editor, and with store-limited admins, anyone holding a store's
admin role can put HTML in front of that store's customers.

The storefront's CSP stops inline script, but not a fake sign-in form or a phishing link. So the
body passes through an allow-list sanitizer (`HtmlSanitizer`, MIT): headings, paragraphs, lists,
emphasis, links with `https:`/`mailto:`/relative targets, and images over `https:`.

A hand-written sanitizer is the classic bug here, which is what justifies the dependency.

**As built: on save only, in `StockApi`.** Rendering through it as well was the plan, and it
cannot be done here.

- **The conflict.** Every stable HtmlSanitizer release either pins AngleSharp 0.17 or needs 1.7
  or later. bUnit 1.40, which the storefront's tests use, breaks on either: the existing tests
  failed with `MissingMethodException` on `IHtmlCollection<T>.get_Item`, the trap CLAUDE.md
  records.
- **Why bUnit stays.** It is pinned at 1.40 to keep the solution on xunit v2.
- **What that leaves.** The sanitizer lives in `StockApi`, the one path the portal writes
  through, and the editor shows the cleaned body back. Rows written by SQL are trusted, as
  every row was before T9.
- **Revisit when bUnit moves.**

### 2f. A store is created inactive and activated against a checklist

Creating a store writes an inactive `Site` row, which the storefront does not serve and an
all-stores admin can act for (fixes 1j). Activation is a procedure that refuses, listing what is
missing, until the store has:

- a domain, a sending address and an operator address;
- a tax rate above zero and a VAT number;
- a legal name;
- content for `terms`, `privacy` and `cookies`;
- at least one active category.

A store with a 0% rate charges nothing, and a store with no legal pages should not take
customers. Deactivating is allowed, behind a confirmation that says the storefront will 404.

---

## 3. Items

Built in this order. Each item lists what proves it.

### 1. Check-constraint drift (0.5 day)

- Rewrite each of the nine constraints in the form SQL Server stores it: the `OR` chain, in the
  stored order, with the stored literal prefix. Each gets a comment, because the form looks
  wrong and someone will "tidy" it back to `IN`.
- **Proof — built.** An empty scratch database was published twice from the old source. Its
  deploy report still dropped and re-created exactly those nine and planned nothing else. After
  the rewrite, the report against that same database is empty. CI's sequence on a second empty
  database leaves the second publish touching no constraint and the report empty. The
  development database's report is empty too.
- **The guard:** CI's `database` job already publishes to an empty database twice. It gains a
  `/Action:DeployReport` after the second publish and fails if the report contains any
  operation — "a publish to an up-to-date database does nothing" becomes a test, and the next
  drift fails CI instead of production's maintenance window.

### 2. Store-limited admins (3–4 days)

- **Schema.**
  - `dbo.UserSite`, with a composite primary key and foreign keys to `User` and `Site`.
  - `User.AllSites`, appended last, plus the `Seed.sql` step from 2a.
  - `spUserSite_CanAct`, and procedures to set a user's grants that refuse to remove the last
    `AllSites`.
- **Middleware.** After resolving a store, `AdminSiteResolutionMiddleware` checks the
  authenticated user can act for it, including on the single-store fallback; otherwise the
  store stays unresolved.
- **The store list.** `GET api/Site` lists only the stores the caller may act for. For an
  all-stores admin, inactive stores are included and flagged.
- **An `AllStores` authorization requirement** reads `User.AllSites` and guards:
  - the user and grant actions;
  - store creation, activation and `Domain`;
  - `PurchaseController.GetPurchaseReport` and `InventoryController`;
  - and, until item 4a gives products an owner, `ProductController`'s shared-product actions.

  `AuthorizationSurfaceTests` gains a list of actions that must carry it, for the same reason it
  lists open actions: the thing that matters is an absence.
- **Users screen.**
  - It lists staff from `dbo.User`, never `AspNetUsers`.
  - Roles are changed by user id, not by email (1k).
  - Each user gets an "All stores" switch or a set of store checkboxes.
- **Portal.** Sign-out clears `adminSiteKey`, and `SwitchSiteAsync` refuses a key it was not
  offered.
- **Proof:**
  - Middleware tests: granted, refused, all-stores, revoked between two requests, and no header
    with one active store.
  - An E2E journey across the two stores the suite already resolves. A store-limited admin sees
    one store, and a forged header gets the unknown-store 400. An all-stores admin switches
    between both.
- **Built.** What building added to the list above:
  - **Sign-out reloads the app.** The portal kept its snapshot in memory across sign-out, so
    the next person to sign in at the same browser saw the previous admin's stores and data.
  - **The rule binds Admins only.** `AllStores` passes non-admins, so a till Manager keeps the
    sales report.
  - **An admin with no store gets a notice, not an error.** They get a "no store yet" notice
    and an empty workspace, rather than "the API could not be reached", and can still change
    their password.
  - **Proven on scratch databases:**
    - An upgrade from the previous schema gave existing users `AllSites`.
    - A later user stayed off, and republishing did not repeat the backfill.
    - Every refusal in `spUserSite_Set` behaved as designed.
    - A fresh publish run twice left an empty deploy report.
  - **Tests:** `StoreAccessJourneyTests` is the eleventh journey.

### 3. Admin screens (2–2.5 weeks)

**3a. Store settings — `/admin/store`.**

- `spSite_Update`, checked by both the controller and the procedure:

  | Field | Rule |
  | --- | --- |
  | `SiteKey` | read-only: it names the theme folder |
  | `Domain` | all-stores only, with a warning that DNS and the domain binding must move with it |
  | `CurrencyCode` | locked once the store has a feed, product, account or quote |
  | `Locale` | must be a known culture |
  | The four keys | from `SiteSettingKeys` (2d) |
  | Percentages | 0–100 |
  | `FeedStaleAfterHours` | ≥ 0 |
  | Addresses | email-shaped |

- **Legal identity.** New columns `LegalName`, `CompanyRegistrationNumber` and
  `RegisteredAddress`, appended last. Together with `TaxRegistrationNumber` they appear in the
  footer's legal strip and on `DocumentSheet` (1g).
- The screen says the storefront sees a change within `Sites:CacheDuration` (five minutes) and
  the API within one. No cache invalidation: a settings change is rare and not urgent.

**3b. Store categories — the existing `/admin/categories` gains them.**

- Create, rename, edit the blurb, set the sort order, deactivate.
- The slug is editable while the store is inactive and read-only after, because it is in
  customers' bookmarks (`SiteCategory.sql:10-11`).
- No delete: a category that is mapped or holds an override is deactivated instead, which
  `fnSite_ProductPlacement` already honours.
- Procedures `spSiteCategory_Insert` and `spSiteCategory_Update`, scoped by `SiteId`.

**3c. Content pages and the home page — `/admin/content`.**

- The content keys move into one list, `SiteContentKeys`, which both `ContentPage`'s routes and
  the screen read, so the two cannot disagree. `home` joins it.
- Per key: `Title`, `Lede` and `BodyHtml` in a plain textarea, sanitized (2e). The screen edits
  the store's default-locale row. Per-locale rows remain possible by SQL, and no store needs
  them yet.
- `spSiteContent_Save` upserts.
- **The home page renders:**
  - the `home` row as its hero;
  - the store's active categories, with their blurbs, as the category grid;
  - the first eight of the catalog's default sort, which puts `SiteProduct.Featured` first, as
    the featured strip.

  The placeholder text and the stub go.

**3d. Email wording — `/admin/email`.**

- One row per `EmailTemplates.All` key, showing the platform's subject and body, the
  placeholders the message has, and which are required.
- Saving runs `EmailRenderer.WhyRefused` and refuses with its reason, so a broken template is
  caught when someone saves it, not by a warning in the dispatcher's log at send time.
- "Use the platform's wording" deletes the row.
- `spSiteEmailTemplate_Save` and `spSiteEmailTemplate_Delete`.

**3e. Stores — `/admin/stores`, all-stores admins only.**

- Create a store: key, name, domain, country, currency, locale, field set and rule set. The key
  is lower-case letters, digits and hyphens, and is fixed once created.
- The admin middleware resolves inactive stores for all-stores admins; the storefront still does
  not (1j).
- Activate and deactivate through the checklist (2f).

**Proof:**

- **Unit:** each procedure's refusals, the sanitizer's allow-list, and the `SiteSettingKeys`
  tripwires.
- **E2E:** a store is created in the portal, configured, refused activation with a list, then
  activated, after which its storefront serves the content an admin typed. This replaces
  `SqlTestData`'s raw inserts for at least that journey.

**Built**, a commit per sub-item. What building changed or added:

- **The sanitizer runs on save only**, for the bUnit reason recorded under 2e.
- **The domain has to be a bare host name**, and the settings screen refuses anything else: a
  scheme or a port would make the store match no request.
- **A save writes the row a customer reads.** The content save targets the locale row when one
  exists, rather than the agnostic one.
- **`AddTaxRuleSets`, `AddRegistrationFieldSets` and `AddOrderingModes`** are what
  `SiteSettingKeysTests` resolves through, so a key with nothing behind it fails a test. So
  does a page route with no key.
- **Two T-SQL traps found on scratch databases.** A `VALUES` list cannot see an outer alias,
  and the DACPAC builds anyway. A `%` in a `THROW` message empties it.
- **Proof:**
  - Each procedure's refusals were exercised on scratch databases.
  - The routine filter is green on four projects.
  - `NewStoreJourneyTests` is the twelfth journey, and the suite passes 12 of 12.

### 4. A second country (2–2.5 weeks, plus 4b if the file differs)

**4a. Product provenance and currency (2b, 2c).**

- **Pre-deployment** (guarded by `OBJECT_ID`, so it does nothing on an empty database) adds and
  fills `Product.FeedId` and `Product.CurrencyCode`:
  - `FeedId` matches `Distributor` to a feed name where exactly one feed has it. Ambiguous rows
    stay NULL and are counted in the output.
  - `CurrencyCode` comes from the feed's store for distributor rows. Other rows get the
    database's currency when every store shares one, and the publish stops with a message
    otherwise.
- `FK_Product_ToDistributorFeed` is `ON DELETE SET NULL`: deleting a feed stops its stock
  selling, and quote and order history still resolve.
- **The feed merge.** `spProduct_BulkUpsertFromFeed` takes `@FeedId` and merges and delists on
  `(FeedId, DistributorSku)`. It keeps writing `Distributor` as the feed's current name, on
  update as well, because brand aliases and `ExcludeDistributor` read it.
- **One function decides what a store can sell.** `fnSite_ProductSource` is the placement
  predicate from 2b, used by:
  - `fnSite_ProductPlacement`;
  - the Categories screen's two procedures (1b);
  - `ProductController`'s checks, which replace item 2's interim all-stores gate. A product
    outside the acting store's source set is a 404.

  One definition, so the shop, the categories screen and the product editor cannot disagree.
- **Currency follows the store.**
  - Currency comes from the store in `spAccount_Insert`, `spQuote_Insert` and `spOrder_Insert`,
    and the portal selects go.
  - The products screens, the dashboard and `spReport_GetSales` use the acting store's currency
    instead of `"EUR"`.
- **The desktop POS has no store**, and `spProduct_GetAll` returns every product, so a GBP feed
  would appear on the till. `Pos:CurrencyCode` (default `EUR`) filters it, as one setting for
  the deployment.
- **Proof:**
  - Database-backed tests: the merge and delist SQL, for the first time (1c); a renamed feed
    keeps its products; two stores with same-named feeds stay apart; a GBP store's catalog holds
    no EUR product and the reverse; documents take their store's currency.
  - Re-run `CatalogLoadCheck` and record the numbers against T7 §7, because placement gains a
    join.
- **Built.** It differs from the plan in four ways:
  - **No pre-deployment script.** `Product.CurrencyCode` defaults to `'EUR'`, like
    `Site.CurrencyCode`, `Account.Currency` and `Quote.Currency` beside it, and every production
    write sets it. `Seed.sql` then backfills `FeedId` from the feed name and a feed's products to
    its store's currency.
  - **A predicate function, not a set.** It is `fnSite_ProductSellable(@SiteId, @FeedId,
    @Source, @CurrencyCode)`. A function returning a set to join would have scanned
    `dbo.Product` a second time under every catalog query.
  - **The admin list leaves out another store's products altogether**, rather than marking them
    "Elsewhere".
  - **The rule keys on `FeedId`, not `Source`**, which an admin can rewrite.
- **Load check (2026-10-06), against T7 §7, same machine and data.** Store A's 50,000 products
  now come from its own feed:

  | Query | p95 now | p95 in T7 |
  | --- | --- | --- |
  | Browse, featured | 282 ms | 309 ms |
  | Browse, one category | 201 ms | 125 ms |
  | Search, a word | 88 ms | 107 ms |
  | Search, a SKU prefix | 62 ms | 80 ms |
  | Price sort, priced group | 1,028 ms | 843 ms |
  | Page 400 | 310 ms | 382 ms |
  | Facets, unfiltered | 223 ms | 163 ms |
  | Facets, searched | 91 ms | 88 ms |
  | Product page | 8 ms | 8 ms |

  - **Why it was first written differently.** The first version wrapped `EXISTS` subqueries in a
    `CASE`. The optimiser could not turn those into joins, and every store-wide query roughly
    tripled — the featured browse reached 943 ms. Rewritten as `fnSite_ProductPlacement`'s own
    `LEFT JOIN` shape, the numbers above are within this laptop's run-to-run spread of T7's,
    except category browse and unfiltered facets.
  - **What the decision is still about.** The T7 §7 decision is unchanged and still open, and
    nothing here changes what it is about.
- **Proof:**
  - `ProductProvenanceTests`, six database tests: the merge and delisting by feed, a rename, two
    stores with same-named feeds, two currencies on one category name, own stock and orphans, a
    product relabelled "Own", and documents in the store's currency.
  - The 277 database-backed tests pass against a database upgraded from the item-3 schema. The
    upgrade appends the columns without rebuilding the table, and the deploy report is empty
    after it.

**4b. The UK distributor's file.**

- **Remove the FlexIT fallback** in `ToSettings()` (1d). `Seed.sql` first writes FlexIT's names
  into any existing feed's blank fields, so today's feed reads exactly what it reads now. After
  that a blank field means unmapped.
- **If the file is XML over SFTP**, it is configuration: the field names go in the feed form.
- **If it is not** (CSV, JSON, HTTP), it needs `DistributorFeed.Format` and a second
  `IDistributorFeedClient` chosen by it. That is built when the file specification and a sample
  file are in hand, and tested against the sample.
- **Connecting to the live SFTP server is the operator's decision**, not part of this build.
- Confirm with the distributor which column is cost and which is the recommended retail price,
  and whether either includes VAT.
- **Built: the fallback is gone.**
  - **The backfill is pre-deployment, not `Seed.sql`.** `Seed.sql` runs on every publish and
    would have overwritten a UK feed's deliberately blank field with FlexIT's names each time.
    `FillLegacyFeedFields.sql` runs once instead, guarded on `Product.FeedId` not existing yet,
    which marks a pre-T9 database.
  - **Proven on a scratch database.** An upgraded legacy feed kept its own mapping and gained
    FlexIT's names for its blanks. A blank set afterwards survived a republish, and a new feed's
    blanks were left alone.
  - `FeedFieldMappingTests` holds the mapping.
- **Not built: a second file format.** The UK distributor's specification is not in hand
  (§7 question 5).

**4c. The `uk-b2b` tax rule set.**

| Customer | Treatment | Rate |
| --- | --- | --- |
| Product marked exempt | Not taxable | 0 |
| United Kingdom, including Northern Ireland, or no country | Domestic standard | `Site.StandardTaxRatePct`, 20 for aclitech |
| Anywhere else, the EU included | Export | 0, legend "Zero-rated export. No UK VAT is chargeable." |

- The existing `TaxTreatment` values are reused, and no CHECK constraint needs widening.
- Registered through the `AddTaxRuleSets()` extension (2d), so both hosts gain it in one line.
- **Not built, and a question for the accountant:** the UK domestic reverse charge on mobile
  phones and computer chips. It applies to supplies of £5,000 or more to VAT-registered
  businesses, which an IT reseller can reach. If it applies, it is a per-line decision and a
  phase of its own.
- This is an engine, not tax advice, as `EuB2bTaxRuleSet` says of itself. Both stores' treatment
  needs the accountant's sign-off before launch (A4, B4).
- **Proof:** a decision-table test in the shape of `TaxRuleSetTests`, and the GBP E2E journey
  (item 4e).
- **Built**, as `UkB2bTaxRuleSet` and `UkTaxRuleSetTests`. Ways of writing the United Kingdom —
  `GB`, `UK`, `United Kingdom`, the four nations, `XI` — and a blank country are domestic. Every
  other country, Ireland and the EU included, is an export, VAT number or not.

**4d. The `uk-b2b` registration field set.**

- **Required:** Company, first and last name, email, phone, address line 1, city, postcode,
  country.
- **Optional:**
  - VAT number — many small businesses sit below the UK registration threshold.
  - "Companies House number" — sole traders and partnerships have none.
  - Address line 2, and "County".
- **Shape checks.**
  - A VAT number is `GB` followed by 9 or 12 digits, or `GBGD`/`GBHA` followed by 3 digits.
  - A Companies House number is eight characters: eight digits, or two letters and six digits.
  - Both block on a malformed value. A VAT country that differs from the address is advisory,
    as in `eu-b2b`.
- **Documents.** A required document proving the business exists, stored under the existing
  `ChamberOfCommerce` kind. The kind's name is an EU misnomer, and its display labels
  (`Account.razor:198`, `AccountDetail.razor:440`) become "Company registration document". The
  VAT certificate is optional.
- **Proof:** tests in the shape of `EuB2bRegistrationFieldSetTests`, and
  `TenantVariationTests` extended to render both field sets.

**4e. A journey across both countries.**

- On the GBP store, with products inserted under a GBP feed:
  - a UK customer sees only those products, in pounds, asks for a quote, and is priced by an
    admin acting for that store;
  - the customer accepts, and the order carries GBP, 20% and its legend.
- An Irish customer of the same store is zero-rated as an export.
- The EUR store's catalog shows none of it.

### 5. The template and tenant split (1–2 days)

The template is already clean: ACL is named only in comments and tests, and the only theme is
`default`.

- `docs/runbooks/new-store.md`:
  1. Create the store in the portal.
  2. Add its theme files under `SMStore/wwwroot/sites/{SiteKey}/` in the downstream repo.
  3. Add its domain to `Deployment:StoreDomains`, with its certificate parameter, and set up
     DNS.
  4. Verify its sending domain with ACS.
  5. Add its distributor feed and credentials.
  6. Work through the activation checklist.
- The downstream repo's layout, and the path guard its CI runs: fail any pull request touching
  files outside its own `SMStore/wwwroot/sites/{key}/`, deployment configuration and workflows.
  The guard is a snippet in the runbook; the workflow belongs to the downstream repo.
- `CLAUDE.md` and the implementation plan, brought up to date as each item lands.

---

## 4. Start now — the person's steps

These have lead times longer than the build:

- **Domains.** Register `.ie` (IEDR identity verification — the classic blocker) and `.co.uk`
  (Nominet, quick).
- **The UK distributor.** A trading account, the file specification, a sample file and the SFTP
  credentials. The first live connection is the operator's to authorize.
- **Is aclitech a separate UK company?** That decides its legal name, company number and
  registered office (3a). It also decides whether it needs its own **UK VAT registration with
  HMRC**, which takes weeks and must exist before the store can charge VAT.
- **An accountant covering Ireland and the UK**, with the questions in §7.
- **The UK ICO data-protection fee** for the UK business.
- **ACS sending domains** verified for both stores.
- **From T7:** the staging deploy, the staging domains' DNS and the restore drill
  (`docs/runbooks/production.md`).
- **The two repositories:**
  1. Tag this repo `template-v1`.
  2. Create the private downstream repo, push `main` to it, and add this one as `upstream`.

  GitHub will not fork a repo into the account that owns it, and its "template repository"
  copy loses the history that `git merge upstream/main` needs.

## 5. Out of scope

- **Exchange rates, and more than one currency within a store** — still out of v1.
- **Per-product price overrides** — v1.1, as planned.
- **Live VAT-number validation** — VIES and HMRC; v1.1, as planned.
- **A rich-text editor, email previews, and theme upload** — themes stay as files in the
  downstream repo.
- **The UK domestic reverse charge** — see 4c.
- **Faster catalog browsing at 50,000 products, and paging the portal snapshot** — the T7 §7
  decision, still open.
- **Hiding navigation links to content pages a store has not written** — "Solutions" is linked
  from every store's header. A small follow-up if a tenant objects.

## 6. Estimate

| Item | Effort |
| --- | --- |
| 1. Constraint drift | 0.5 day |
| 2. Store-limited admins | 3–4 days |
| 3. Admin screens | 2–2.5 weeks |
| 4. Second country | 2–2.5 weeks, plus 3–5 days if 4b needs a new client |
| 5. Split | 1–2 days |

**About 5–6 weeks.** The two tenant tracks follow, and mostly overlap each other, since their
content and taxonomy work is independent.

## 7. Open questions

**For the accountant:**

1. **Northern Ireland on the Irish store.** Under the Windsor Framework, Northern Ireland follows
   EU VAT rules for goods. `eu-b2b` zero-rates every GB customer as an export. A Northern Irish
   business with an `XI` VAT number is probably an intra-EU reverse-charge customer instead, and
   one without a VAT number probably pays Irish VAT. If confirmed, the change is an `XI` branch
   in `EuB2bTaxRuleSet` and a test; it is not built until confirmed.
2. **The UK domestic reverse charge** on phones and computer chips (4c).
3. **Sign-off on both rule sets** as configured.

**For the operator:**

4. **Should aclitrade.ie accept GB businesses?** Today it cannot (1f). If yes, `eu-b2b` accepts
   a `GB` prefix as a non-EU number, and those customers are zero-rated exports. If no, the form
   should say so and point them at aclitech.co.uk, rather than calling their VAT number
   malformed.
5. **Which file format** does the UK distributor deliver (4b)?
