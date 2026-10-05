# T8 — Store catalog control

Detail plan for the eighth template phase, added to
`docs/plans/2026-09-10-storefront-implementation-plan.md` on 2026-10-05 from two requirements:

1. **In the admin panel, the admin chooses which products show on each storefront.**
2. **Prices are hidden from visitors who are not registered.** Only signed-in customers see them.

T0–T6 are merged. T8 runs before T7, because T7 hardens what exists, and nothing in the tenant
track starts until the template track finishes.

**Exit:** an admin hides one product and shows another on one store without changing the other
store, and both the storefront and the admin screen agree on what that store sells. An
anonymous visitor to a store set to `Authenticated` sees no price and no price ordering
anywhere. A signed-in customer sees prices whether or not their account has a pricing group.

**Status: planned.** Nothing built.

---

## 1. What is already wrong

As in T5 and T6, reading the path before building on it found problems, and most of them change
the shape of the work.

### 1a. "Published" is one switch for every store

`Product.Published` exists, and its own comment says it means "we choose not to sell it". But
`dbo.Product` is shared by every store — `CategoryMapping.sql` says so: one physical product may
be sold by two stores, and the POS has no site at all. So setting `Published = 0` for one store
removes the product from all of them. `Featured` (the default sort, the home page selection)
and `Badge` (the ribbon on the tile) have the same problem: the plan's A2 calls curating them
tenant work, but where they're stored, store two's "Best seller" ribbon appears on store one.

Nothing writes any of the three. `AdminProductModel` doesn't carry them, `spProduct_Update`
doesn't set them, and the portal has no control for them. They hold their defaults
(`Published = 1`, `Featured = 0`, `Badge = NULL`) unless someone edited a row by hand.

By the platform's own rule — a tenant task that needs a change to shared code is a template
gap — these are store decisions stored as platform data. They move to a per-store row (§2).

### 1b. The admin's product form can take a product off every store

`SMPortal/Pages/Admin/Products/ProductDetail.razor` offers a hardcoded list of seven categories
and saves the choice into `Product.Category`. That column is the **feed's** category string —
the value `CategoryMapping.FeedValue` is matched against. Saving the form on a product whose real
category isn't one of the seven rewrites it to one that probably has no mapping, and the product
disappears from every store without a warning. CLAUDE.md already says not to build on that
control. T8 removes it: a product's feed category isn't the admin's to edit, and where a product
appears on a store is (§2).

### 1c. A signed-in customer without a pricing group sees no prices

`CatalogPresenter.ShowPrices` is:

```csharp
!string.Equals(_siteContext.Site.PriceDisplay, "Authenticated", ...) || CustomerGroupId is not null;
```

It checks for a **pricing group**, not a sign-in. A group is optional — `spAccount_Approve`
keeps whatever the account already has, which may be nothing — so an approved customer with no
group is treated as anonymous on an `Authenticated` store. Requirement 2 is "registered users see
prices", and this breaks it for exactly the customers who haven't been put in a group yet.

### 1d. Sorting by price ranks prices for people who may not see them

`Catalog.razor` always offers "Price ↑" and "Price ↓", and `spCatalog_Search` honours
`price-asc` / `price-desc` for any caller. The cards hide the amount, but the order still tells
an anonymous visitor which product is cheaper. A trade-only store that hides its prices has
still given away the ranking.

### 1e. Nothing writes `GroupVisibility`

The per-group rules (`IncludeCategory`, `ExcludeCategory`, `ExcludeDistributor`) are enforced in
`fnCatalog_VisibleProducts`, as T2 planned. Nothing in StockApi or the portal reads or writes
them, so they only exist if someone inserts rows. Recorded here because it's the nearest
neighbour of requirement 1. It's a different requirement (per customer group, not per store),
so it stays out of T8 (§8).

### 1f. Two documents say admin reads aren't scoped. They are.

CLAUDE.md's section "Scoping is done on writes and not on admin reads", and the T8 entry in the
implementation plan, both say StockApi has no site concept. That stopped being true in T3:
`AdminSiteResolutionMiddleware` reads the acting store from `X-Site-Key`, `IAdminSiteContext`
throws if none was named, and nine controllers read through it (`spAccount_GetAll`,
`spQuote_GetAll` and `spDistributorFeed_GetAll` all take `@SiteId`). Admins are **global**: any
admin may act for any active store, and `AdminSiteContext` says per-site admins only need a
"may this user select this site" check.

This removes the prerequisite the implementation plan gave T8. A per-store toggle reached
through the acting-store header is scoped like everything else. Whether an admin should be
limited to some stores is still undecided, and it's still a prerequisite for a **second tenant**
— but it isn't one for T8. Both documents are corrected in item 1.

---

## 2. The first decision: how an admin chooses a store's products

Today a product is on a store if all of these hold: `Published = 1`, `Delisted = 0`, its feed
`Category` has a `CategoryMapping` row for the store, that row points at an active
`SiteCategory`, and the viewer's group rules don't exclude it.

### Not an allow-list

"Only the products I've ticked" is the obvious reading of the requirement, and it's wrong for a
feed-backed catalog. Feeds add products nightly. Under an allow-list every new SKU is invisible
until somebody ticks it, and the catalog quietly goes stale — the failure T4 spent a phase
making visible.

### Decision: the category mapping is the default, and a per-store override handles exceptions

```
SiteProduct
    SiteId, ProductId                PK
    Visibility    'Show' | 'Hide' | NULL   -- NULL: follow the category mapping
    SiteCategoryId                   NULL  -- where a Show product appears when its feed
                                           -- category is unmapped, or to move it
    Featured      BIT
    Badge         NVARCHAR(40) NULL
    UpdatedUtc
```

- **Hide** removes a product whose category maps. It's what curating a feed mostly is.
- **Show** with a `SiteCategoryId` places a product whose feed category isn't mapped, without
  mapping the whole category. A shown product has to appear under some category, because the
  catalog joins it for slugs, facets and breadcrumbs. So Show without a category is allowed
  only where the mapping already gives one.
- **No row** means "follow the mapping". The table holds exceptions, not the catalog. An
  allow-list would need a row per product per store.
- **Bulk by category is mapping, not override.** "Put all of this feed category on my store"
  is one `CategoryMapping` row, and new SKUs in it appear automatically. "Hide all of it" is
  removing the mapping. T8 adds a mapping screen (item 5) so this needs no SQL.
- `Featured` and `Badge` move here from `Product` (§1a). They're store decisions, and this is
  the store's row about the product.
- Composite foreign key `(SiteCategoryId, SiteId)` → `SiteCategory(Id, SiteId)`, as
  `CategoryMapping` has, so a store cannot place a product in another store's category.
- `Product.Published`, `Featured` and `Badge` are **dropped**, after a pre-deployment backfill
  turns `Published = 0` into a Hide row on every store and copies any `Featured`/`Badge` that
  were set onto the stores that map the product. Nothing writes them today, so the backfill
  should have no rows to move — but a hand-edited row should survive the change rather than
  vanish with it.

**Why drop rather than keep a global switch:** two ways to hide a product means a reader never
knows which one is in force, and "hide it from every store" is a loop over Hide rows that the
admin can see per store. And **no production database exists yet**, so this is the cheapest the
drop will ever be.

### One function decides where a product sits on a store

```
dbo.fnSite_ProductPlacement(@SiteId) → ProductId, SiteCategoryId, OnStore, Reason
```

Inline, so it expands into the caller's plan like `fnCatalog_VisibleProducts`. Two callers:

1. `fnCatalog_VisibleProducts` replaces its `INNER JOIN CategoryMapping` with it. The listing,
   facets, detail page, `spBasket_AddLine` and `spBasket_GetLines` already go through that
   function, so they follow automatically.
2. **The admin's product read for the acting store.** The admin sees "on this store / hidden /
   not mapped" from the same predicate the storefront uses, not from a re-derivation in C#. Two
   copies of "is this product on the store" is how the screen says yes while the shop says no.

`Reason` is for the admin: `Mapped`, `Shown`, `Hidden`, `Unmapped`. The storefront only reads
`OnStore`.

`spQuote_SubmitRequest` re-checks products through `CategoryMapping` directly — on purpose, so
one atomic write is checked on its own terms. That check moves to `fnSite_ProductPlacement` too,
or a hidden product could still be submitted from a basket that held it before it was hidden.

### Hiding is not withdrawing

A hidden product stays on any quote or order that already contains it, exactly as a delisted
one does. `spQuoteLine_GetByQuote` and `spOrder_ConvertFromQuote` don't read placement, and
mustn't start. `DelistedProductHistoryTests` is the model: a test that hides a product on a
priced quote and asserts the quote still renders and converts.

A hidden product already in a basket comes back `Available = 0` from `spBasket_GetLines` and is
dropped and named at submit. That's existing behaviour for a product that stops being visible,
and it applies here unchanged.

---

## 3. The second decision: what "registered" means for prices

Only an **approved** account can sign in — `CustomerAuthEndpoints` and `CustomerSessionValidator`
both refuse anything else — so "registered user" and "signed-in customer" are the same set
today. `ShowPrices` therefore becomes:

```csharp
PriceDisplay is not "Authenticated" || _customer.IsSignedIn
```

The group still decides which price a customer sees; it never decides whether they see one.

If a store later wants pending applicants to see prices, that's a change to the sign-in gate,
the same as `IOrderingMode.RequiresApprovedAccount` — not to this line.

**Price sort follows the same rule.** `CatalogPresenter` drops `price-asc` / `price-desc` from
the sort options when `ShowPrices` is false, and maps a posted price sort back to the default.
The procedure stays as it is: the presenter is its only caller, and pages never reach
`ICatalogData`.

**No admin screen for `PriceDisplay` in T8.** It's one column on `Site`, which has no admin
screen for any of its columns. aclitrade sets it once in A0. A store settings page belongs with
the rest of `Site` (§8).

---

## 4. Admin surfaces

All on the store the portal's site switcher is acting for, through `IAdminSiteContext`.

- **Products list.** Adds an "On this store" column from `fnSite_ProductPlacement` showing
  the reason (`Mapped`, `Shown`, `Hidden`, `Unmapped`), a toggle per row, and a bulk
  hide/show over the current selection. The list is the existing in-memory snapshot:
  `GetCatalog` gains the acting store's placement columns and keeps its shape otherwise.
- **Product detail.** The category dropdown goes (§1b). In its place: this store's visibility,
  the category it appears under (read-only when it comes from the mapping, a choice when it's
  shown by override), Featured and Badge.
- **Category mapping.** Each feed category value with its product count, and which of this
  store's categories it maps to, or none. One select per row. It picks from the store's
  **existing** `SiteCategory` rows; creating and renaming store categories is out of T8 (§8).

API, all `[Authorize(Roles = "Admin")]` and acting-store scoped:

```
GET  api/Product/Catalog                     + placement for the acting store
PUT  api/Product/Catalog/{sku}/Placement     visibility, site category, featured, badge
POST api/Product/Catalog/Placement           bulk: skus[] + visibility
GET  api/CategoryMapping                     feed values, counts, current mapping
PUT  api/CategoryMapping/{feedValue}         site category, or none to unmap
```

The desktop POS calls `GET api/Product` and has no site. That action stays global, as
`ProductController` already arranges by injecting `IAdminSiteContext` per action rather than
through the constructor.

Every write procedure takes `@SiteId` and checks that a `SiteCategoryId` belongs to it. The
composite key refuses the mismatch too, but a foreign-key violation inside a procedure reaches
the portal as a 500, not as an answer — the `spAccount_Approve` / `CustomerGroup` precedent.

---

## 5. Schema

```
SiteProduct                                      -- new
    SiteId, ProductId                PK (SiteId, ProductId)
    Visibility      NVARCHAR(10)  NULL  CK: NULL | 'Show' | 'Hide'
    SiteCategoryId  INT           NULL  FK (SiteCategoryId, SiteId) → SiteCategory(Id, SiteId)
    Featured        BIT           NOT NULL DEFAULT 0
    Badge           NVARCHAR(40)  NULL
    UpdatedUtc      DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
    CK: Visibility = 'Show' requires a category, unless the product's feed category maps
        -- checked in the write procedure; a CHECK cannot see CategoryMapping

Product
    - Published, Featured, Badge     -- backfilled into SiteProduct by a pre-deployment script
    ~ IX_Product_CatalogGate         -- leads on (Delisted, Category); Featured leaves the INCLUDE

fnSite_ProductPlacement(@SiteId)                 -- new, inline
fnCatalog_VisibleProducts                        -- joins placement instead of CategoryMapping
spQuote_SubmitRequest                            -- re-checks through placement
spProduct_GetCatalogForSite, spSiteProduct_Set, spSiteProduct_SetMany,
spCategoryMapping_GetForSite, spCategoryMapping_Set     -- new
```

Per the rules CLAUDE.md now carries: the new table's columns are its own, so order doesn't
matter there, but `Product` loses three columns, which is a change DacFx makes with an `ALTER`.
Run `grep -i "rebuilding table"` on the publish anyway.

---

## 6. Work items, in order

**1. Correct the record, and fix the price gate.** The two stale descriptions of admin
scoping (§1f). `ShowPrices` checks the sign-in (§1c). Price sort is dropped and coerced for a
viewer who can't see prices (§1d). Tests: a signed-in customer with no group sees list price;
an anonymous visitor to an `Authenticated` store gets no price on a card, the detail page or the
basket, and no price sort option, and `?sort=price-asc` falls back to the default. Small and
independent, so it goes first and requirement 2 is closed early.

**2. `SiteProduct`, the backfill, and the drop.** Table, pre-deployment backfill, drop
`Published` / `Featured` / `Badge`, rework the index. Update the twelve test fixtures that insert
`Published`. The featured sort and the badge read from `SiteProduct`.

**3. Placement.** `fnSite_ProductPlacement`; `fnCatalog_VisibleProducts` and
`spQuote_SubmitRequest` read through it. Database tests: a hidden product is absent from search,
facets and the detail page, refused by `spBasket_AddLine`, comes back unavailable from
`spBasket_GetLines`, and is dropped at submit; a shown product with a category appears under
it; a hidden product on a priced quote still renders and converts; one store's override
doesn't reach another store. `CatalogPriceParityTests` and the staleness tests still pass
unchanged — they're the evidence the join rework didn't change pricing or staleness.

**4. Admin API.** Placement read and writes; mapping read and write; procedures that refuse a
category from another store with a named error, not a foreign-key 500.

**5. Portal.** Products list column, toggle and bulk action; product detail without the
dropdown; the mapping screen. bUnit tests against the substituted `IAdminDataService`.

**6. End to end.** One journey across the two stores the suite already uses (`localhost` and
`127.0.0.1`):
- The admin, acting for store A, hides a product. It's gone from store A's search and detail
  page, and still on store B.
- On store A, the admin shows a product from an unmapped category, under a category, and it
  appears there.
- An anonymous visitor to store A, set to `Authenticated` for the journey and restored after,
  sees no price and no price sort.
- A signed-in customer with no group sees list price.

---

## 7. Risks

- **The catalog query changes shape.** An `INNER JOIN` to the mapping becomes a placement
  function with an override join and a coalesced category, and the featured sort leaves the
  covering index. At today's 1,676 products that's invisible. At the tens of thousands this
  platform is sized for it might not be. T7's load check should include a browse with the
  default sort and a search, before and after.
- **Twelve fixtures insert `Published`.** They break when it's dropped, the way four broke on
  `CK_Account_Terms` in T6. That's mechanical, but it's the item most likely to look like a
  regression in review.
- **The admin snapshot grows a column.** `GetCatalog` loads the whole catalog into the portal
  already. Placement adds a few bytes per row, not a new pattern — but the pattern itself is
  T7's to question at scale.
- **A hidden product can still be in a customer's basket.** It's already handled (unavailable,
  then named at submit), but a customer sees "no longer available" for something the store
  chose to hide. That's accurate, and it's worth knowing before somebody calls it a bug.

---

## 8. Not in T8

- **Admin screens for `GroupVisibility`** (§1e). A separate requirement (per customer group),
  and the enforcement already exists.
- **Creating, renaming and ordering a store's categories** (`SiteCategory`). The mapping screen
  picks from existing ones. A new store gets its categories as tenant configuration (A2) until a
  later phase adds the screen.
- **A `Site` settings screen**, including `PriceDisplay`. Recorded with the other `Site`
  columns that have no screen (CLAUDE.md).
- **Per-site admins.** Admins stay global (§1f). Restricting them is a prerequisite for a second
  tenant, not for this.
- **Per-product price overrides.** v1.1 in the implementation plan.
- **Anonymous output caching.** T2 planned it and it wasn't built. Whoever builds it must vary
  the cache by sign-in on any store that hides prices, or the first signed-in customer's priced
  page is served to everybody.
