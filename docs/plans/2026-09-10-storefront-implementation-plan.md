# B2B Ecommerce Platform — Implementation Plan

Date: 2026-09-10 (restructured into two tracks)
Author: Chadi Hammoud (with Claude)
Companion to: `docs/plans/2026-07-24-aclitrade-b2b-ecommerce-design.md`

The July design document set the target. This one is the build plan: what already exists, what
the remaining work actually is, in what order, and what has to be true before a store is
reachable from the public internet.

**StockManager is being built as a reusable B2B ecommerce platform, not a single site.**
aclitrade.ie is the first store to launch on it and the driver of current requirements — but it
is one tenant among future ones. The work therefore splits into two tracks:

- **Template track (T0–T7)** — the platform. Generic, site-scoped, reused unchanged by every
  future store.
- **Tenant track (A0–A4)** — aclitrade.ie specifically. Configuration, content, brand assets,
  taxonomy data, legal. Repeated per store, never touching shared code.

The tracks interleave: each tenant phase depends on a template phase landing first (§7).

## 1. Where the code stands today

The July design assumed the customer-facing side would grow out of the `SMPortal` stub pages
(`Products` / `Checkout` / `Login` / `Signup`). That is no longer accurate — those stubs were
replaced by the admin portal in `b17b5ba`. **`SMPortal` today is admin-only**: `Pages/Login.razor`,
`Pages/LogOut.razor`, and `Pages/Admin/*`. There is no customer-facing code anywhere in the solution.

### Built and working

| Area | State |
| --- | --- |
| Schema | `Account`, `CustomerGroup`, `Product`, `Quote`, `QuoteLine`, `Purchase`, `DistributorFeed`, `Inventory`, `User` |
| Feed ingest | `SftpDistributorFeedClient` (SSH.NET, host-key pinning), `DistributorFeedSyncService`, staged upsert via `spProduct_SyncFeeds` / `spProduct_BulkUpsertFromFeed` |
| Feed secrets | `IFeedSecretStore` with Data Protection and Key Vault implementations, resolved per feed row |
| Product content | `IcecatClient` + `ProductImageEnricher` + `ProductImageBackgroundService` |
| API | 11 controllers, dual auth (JWT for clients, Identity cookie for Razor) |
| Admin portal | Accounts, Groups, Products, Quotes, Orders, Feeds, Users, Reports, Dashboard — with search, paging, modals |
| Orchestration | Aspire app host, persistent SQL container, DACPAC staleness guard |

That is the entire back office. The quote lifecycle (`Requested → Priced → Accepted/Rejected`) and
quote-to-order conversion (`spOrder_ConvertFromQuote`) already work from the admin side.

`DistributorFeed` is the best existing example of the shape the whole platform should take:
host, credentials, directory and **field mapping** all live in a table, so onboarding a
distributor is configuration rather than code. Extend that pattern, don't invent a new one.

### The Template directory is a specification, not code

`C:\dev\temp\Template` holds 12 Claude Design artboards (`.dc.html`): Home, Catalog, Product,
Quote, Register, Login, Solutions, About, Contact, plus `SiteHeader` / `SiteFooter` /
`ProductCard` components. They render through `DCLogic` with `{{ }}` bindings against a mock
`data/products.js` (USD, invented brands — Corvus, Ferrum, Nimbus, Vantiq, Sentri, Ampere, Halon)
and a `data/quote-cart.js` that persists an RFQ list to `localStorage` and posts nowhere.

Design tokens live in `_ds/` plus `brand.css` (green `#2f7a4a` accent, flat `#f2f2f3` ground).
The logo is `uploads/Glossy Wall Logo Mockup - ACL ITRADE.png`.

It is the visual and behavioural spec for the **first tenant**. The structure it implies is
template work; its colours, copy and category names are tenant work.

### The gap

Everything a customer touches. Concretely: no storefront project, no site resolution, no public
catalog query, no customer identity, no registration, no price resolution, no RFQ submission, no
customer quote or order views, no tax treatment beyond a flat config rate, no transactional
email, no payment.

## 2. Architecture decisions

### How the storefront renders

**Blazor WebAssembly is the wrong host for a public storefront.** Search engines get an empty
`<div id="app">`, first paint waits on a multi-megabyte framework download, and the artboards are
plain HTML/CSS that gain nothing from a client runtime.

**Chosen: a new `SMStore` project — Blazor Web App (.NET 10), static/SSR render mode by default,
interactive islands only where needed** (quote cart, catalog filters, quantity steppers).

Rejected alternatives:

- *Razor Pages / MVC storefront* — works, slightly less componentised. Fall back to this if the
  Blazor Web App render-mode model proves fussy; the rest of this plan is unaffected.
- *Next.js or Astro against `StockApi`* — highest fidelity to the HTML mockups, but adds a second
  language, a second deployment, a second auth implementation, and a CORS surface. Not worth it.

**One `SMStore` deployment serves every site**, resolving the tenant from the request host. Not
one build per store.

### `SMStore` talks to `SMDataManager.Library` directly, not through `StockApi`

It renders on the server, so an HTTP hop to a co-located API buys nothing and costs a token dance
on every anonymous catalog page. This contradicts the current CLAUDE.md line that the library is
*"Shared by the API only"* — update that line when the project lands.

### Cookie auth must share ground with the API

Identity lives in `ApiAuthDb`, owned by `StockApi`. `SMStore` uses the same `ApplicationDbContext`
against `ApiAuthDb` with its own cookie scheme. For sessions to survive across both hosts, the
**Data Protection key ring must be shared and persisted** (to the database or Key Vault) rather
than left to each app's local, per-container default. Two containers with two key rings means
cookies issued by one are unreadable by the other, and every restart silently signs everyone out.

### Three seams that make it a template rather than a site

These are the decisions that are cheap now and structural surgery later.

**1. Site configuration is first-class, not a deferred column.** The July design treated `Site` as
future-proofing — one seeded row, nothing reading it. Under the template framing it is load-bearing
from day one. An `ISiteContext`, resolved per request from the host header, exposes country,
currency, tax rules, locale, theme, enabled features and order mode. Nothing downstream reads a
constant where a site property exists.

**2. Theming is token-driven, never baked in.** The shared stylesheet consumes CSS custom
properties only. Each site supplies its own token file, logo and favicon set as per-site assets.
Porting `brand.css` straight into a shared `site.css` would bake ACL green into the platform —
don't.

**3. Order mode is pluggable.** aclitrade is quote-based (RFQ → priced → accepted → order). A
future store may want direct cart checkout. One `IOrderingMode` abstraction with an `Rfq`
implementation in v1 and a `DirectCheckout` implementation later, selected per site. Building
Phase T5 as "the quote flow" with no seam is the single most expensive assumption to unwind.

### Registration field sets vary by jurisdiction

`Register.dc.html` asks for a VAT number and a Chamber-of-Commerce registration document. That is
an EU-specific set. The registration engine is template; **which fields are shown and required is
per-site configuration**.

## 3. Scope

### v1 — platform capable of multi-store, first store live on aclitrade.ie

- Site resolution, per-site configuration, per-site theming
- Public catalog: browse, filter, search, product detail
- Customer registration with configurable field set, admin approval
- Customer sign-in, account area, addresses
- RFQ ordering mode: cart → submitted quote → admin prices → customer accepts → order
- Per-group flat discount pricing, per-group product visibility
- Tax rule engine, configured for Ireland (23% standard, zero-rate EU reverse charge)
- Invoice / credit-terms payment path (Net-30 on approved accounts)
- Transactional email for every state change
- Scheduled daily feed sync with staleness handling
- Content pages driven by per-site content, not hardcoded markup

### v1.1 — fast follow

- Card payment via Stripe hosted checkout
- `DirectCheckout` ordering mode
- Product specifications and datasheet documents (needs an Icecat licence to be worth the schema)
- Per-product price overrides on top of group discount
- VIES live VAT-number validation (v1 captures and stores the number; admin verifies manually)

### Out of v1

A **second live store**. The platform supports it; standing one up is tenant work repeated, not
platform work. Also deferred, per the July design: multi-currency within one site, Elasticsearch,
EDI/Xero, login-as-customer, B2C storefront.

## 4. Template track

Each phase ends in something runnable. Estimates assume solo work with heavy Claude assistance.

### T0 — Platform foundations (1.5–2 weeks)

Plumbing every later phase stands on. Front-loaded deliberately: the CI fix unblocks the rest.

- Create `SMStore` (Blazor Web App, .NET 10), reference `SMDataManager.Library`, wire into
  `AppHost.cs`; add a `sm-store-standalone` entry to `.claude/launch.json` on port 7260 mirroring
  `sm-portal-standalone`
- **Migrate `SMDatabase.sqlproj` to the `Microsoft.Build.Sql` SDK.** Kills the VS-MSBuild
  requirement, fixes `dotnet build StockManager.sln` failing with `MSB4278`, and is a hard
  prerequisite for CI on a Linux runner
- GitHub Actions: restore, build, test, produce the DACPAC
- Shared Data Protection key ring persisted to `ApiAuthDb`, consumed by both `StockApi` and `SMStore`
- Delete `SMDataManager/` (.NET Framework 4.8, not in the solution) before it can ship in a container
- `Site` table, `spSite_GetByDomain`, host-to-site resolution middleware, `ISiteContext`
- `SiteId` columns across scoped entities (`Account`, `CustomerGroup`, `Quote`, `Purchase`,
  `DistributorFeed`)
- Per-site theme loading: shared stylesheet on custom properties, site token file and assets
  resolved from `ISiteContext`
- `IOrderingMode` abstraction registered, `Rfq` as the only implementation

**Exit:** `SMStore` boots under the app host, resolves a site from the host header, renders in that
site's colours. `dotnet build` works on the whole solution. CI is green.

### T1 — Storefront shell and component library (2 weeks)

The generic half of the design port. No tenant copy in any of it.

- Layouts, `SiteHeader`, `SiteFooter`, `ProductCard`, form controls, pager, breadcrumb
- Page shells with real routing: catalog, product detail, quote, register, login, account area
- Content-page mechanism so Home / Solutions / About / Contact render per-site content rather
  than hardcoded markup
- Responsive behaviour from the artboards' breakpoints (880px catalog, 900px quote, 760px register)
- Interactive islands identified and scoped; everything else stays static-rendered

**Exit:** every page type has a live URL, correct structure, theme-driven appearance, placeholder
content.

### T2 — Catalog, search, pricing (2 weeks)

The largest reusable asset in the platform.

- `ICatalogData` + `spCatalog_Search` / `_GetFacets` / `_GetBySku`
- Full-text catalog and index over `Product(ProductName, Description, Sku,
  ManufacturerPartNumber)`; `CONTAINSTABLE` with a `LIKE` fallback for short terms
- Taxonomy **mechanism**: category and brand facets driven by per-site mapping data (the mapping
  content itself is tenant work — A2)
- `IPriceResolver(product, account)` applying `CustomerGroup.Discount` with a margin-over-cost
  floor, used by both storefront display and admin quote pricing
- `GroupVisibility` rules enforced inside the catalog query, not filtered after the fact
- Output caching on anonymous catalog responses; long-lived cache headers or CDN for images

**Exit:** real SKUs browsable and searchable with correct per-group prices and visibility.

### T3 — Customer identity (2 weeks)

- `Contact`, `Address`, `AccountDocument` tables and data access
- Registration engine with a **per-site field set**: which fields appear, which are required,
  which documents are demanded
- `spAccount_Register` creating `Account` (Pending) + `Contact` + `Address` + Identity user in one
  transaction
- Document upload to blob storage with a content-type allow-list and a size cap (the artboard says
  128 MB — cap far lower, 10 MB, and validate server-side)
- `Customer` role, email confirmation, password reset
- Admin approval on `/admin/accounts` triggers the approval email and enables sign-in
- Account area: profile, contacts, addresses

**Exit:** a stranger can apply against any site's field set, be approved from the admin portal,
and sign in.

### T4 — Feed reliability (1–1.5 weeks)

Mostly finishing what exists.

- Scheduled feed sync hosted service — today `DistributorFeedSyncService` only runs when
  `DistributorFeedController.Sync` is called by hand from the admin portal
- Sync loops over active sites, per the July design §6
- Staleness threshold with configurable auto-hide of stale items from the storefront
- Admin alert on sync failure; sync history retained beyond `LastSyncStatus`
- Delisted-SKU handling verified end to end against historical quote lines

**Exit:** feeds sync nightly unattended; a failed sync is visible and does not poison the catalog.

### T5 — Ordering (2.5–3 weeks)

- `IOrderingMode` fleshed out; `RfqOrderingMode` implemented against it
- Server-side quote cart (persisted per contact; `localStorage` only for anonymous visitors,
  merged on sign-in)
- `spQuote_SubmitRequest`; quote lands as `Requested` in the existing admin queue
- Customer views: quote list, quote detail with accept/reject, order list, order detail
- Accept path calls the existing `spOrder_ConvertFromQuote` — **preserve its
  `Reference IS NOT NULL` predicate**, or portal orders leak into POS history
- Quote and order PDF generation, templated per site
- PO number capture on acceptance

**Exit:** full round trip — cart, submit, price in admin, accept, order exists.

### T6 — Tax, terms, transactional email (2–2.5 weeks)

Engine only. Rates and copy are tenant configuration.

- Tax rule engine: rules resolved from `ISiteContext`, supporting standard rate, zero-rate
  intra-EU reverse charge, and export zero-rate; treatment snapshotted onto the order and
  rendered as invoice legends
- Credit-terms path: credit-limit check at acceptance, terms from `Account.PaymentTerms`
- `EmailOutbox` + a hosted dispatcher; templates per site for registration received, approved,
  rejected, quote received, quote priced, quote expiring, order confirmed, password reset
- Retire `ConfigHelper.GetTaxRate()` / the `taxRate` app setting **on the portal path only** —
  the WPF POS still uses it, so do not remove it outright

**Exit:** correct tax on every document for any configured jurisdiction; every state change sends
the right mail.

### T7 — Production hardening (2 weeks)

Everything in §6 plus:

- Azure SQL with point-in-time restore; DACPAC deploy step in the pipeline
- OpenTelemetry from `ServiceDefaults` wired to Application Insights
- Multi-domain TLS and host binding for the shared `SMStore` deployment
- Per-site legal-page and cookie-consent mechanism (the *content* is tenant work)
- Load check on the catalog at realistic SKU counts

**Exit:** deployed to a staging domain, monitored, restorable, rebuilt from a clean checkout by CI.

**Template track total: ~13–16 weeks.**

## 5. Tenant track — aclitrade.ie

Repeated in miniature for every future store. If any of it requires editing shared code, the
template has a gap — fix the template rather than special-casing the tenant.

### A0 — Site registration and brand (0.5 week, needs T0)

- `Site` row: domain `aclitrade.ie`, country IE, currency EUR
- ACL token file from `_ds` + `brand.css`, logo and favicon set as site assets
- **Start `.ie` registration and IEDR identity verification now** — it has real lead time and is
  the classic launch blocker

### A1 — Content and copy (1–1.5 weeks, needs T1)

- Home, Solutions, About, Contact content from the artboards
- Category names and blurbs, navigation labels, footer details
- Registration field set configured for Ireland: VAT number, company registration number,
  Chamber-of-Commerce document required

### A2 — Catalog taxonomy (1–1.5 weeks, needs T2)

- Map real feed category strings and manufacturer names onto the six site categories and the
  brand facet
- Decide and implement the long-tail rule for SKUs matching none of the six
- Curate featured products and badges

This is data work, not code work, and it is the most underestimated item in the plan (§8).

### A3 — Irish tax and terms configuration (0.5 week, needs T6)

- 23% standard rate, EU reverse-charge rule, export zero-rate
- Net-30 terms and credit limits on the seeded customer groups
- Email template copy and branding

### A4 — Launch (1–1.5 weeks, needs T7)

- Privacy policy, cookie consent, terms of sale, company and VAT details in the footer
- GDPR: data-subject request path, processor DPAs, retention policy
- Accountant sign-off on the VAT treatment
- UAT with two or three real accounts on live feed data

**Tenant track total: ~3.5–5 weeks**, largely overlapping template work.

## 6. Sequencing

| Order | Template | Tenant (parallel) |
| --- | --- | --- |
| 1 | T0 Platform foundations | A0 Site + brand, start IEDR |
| 2 | T1 Shell and components | A1 Content and copy |
| 3 | T2 Catalog, search, pricing | A2 Taxonomy mapping |
| 4 | T3 Customer identity | — |
| 5 | T4 Feed reliability | — |
| 6 | T5 Ordering | — |
| 7 | T6 Tax, terms, email | A3 Irish configuration |
| 8 | T7 Production hardening | A4 Launch |

**Calendar: ~15–19 weeks to aclitrade.ie live**, with the platform reusable at that point.

A harder cut — RFQ only with no customer self-service views, invoice terms only, one
flat-discount group, prices public, admin doing quote follow-up by hand — reaches a usable public
site in **7–9 weeks**, at the cost of leaving T5 and T6 partly manual. Do not cut T0: the site
spine and the three seams are what make the second store cheap, and they are the one thing that
cannot be retrofitted without a data migration.

## 7. Schema changes

All under `SMDatabase/dbo/`. Every new `.sql` file also needs a `<Build Include="..." />` entry in
`SMDatabase.sqlproj` — that project lists files explicitly and silently skips anything missing.
After each change: rebuild the sqlproj, then restart the app host. (Once T0's SDK migration lands,
`dotnet build` is enough.)

### New tables

```
Site              Id, Domain, Country, CurrencyCode, Locale, ThemeKey, OrderMode,
                  RegistrationFieldSet, IsActive
SiteTaxRule       Id, SiteId, Kind ('Standard' | 'ReverseCharge' | 'Export'), RatePct,
                  AppliesWhen, InvoiceLegend
SiteContent       Id, SiteId, Key, Locale, Body            -- content pages, nav labels, blurbs
SiteCategory      Id, SiteId, Slug, Name, Blurb, SortOrder
CategoryMapping   Id, SiteId, FeedValue, SiteCategoryId     -- feed taxonomy → site taxonomy
Contact           Id, AccountId, IdentityUserId, FirstName, LastName, Email, Phone,
                  RoleInAccount ('Admin' | 'Buyer'), Status, CreatedDate
Address           Id, AccountId, Kind ('Registered' | 'Billing' | 'Shipping'),
                  Line1, Line2, City, Region, PostCode, CountryCode, IsDefault
AccountDocument   Id, AccountId, Kind, FileName, ContentType, SizeBytes, StorageKey, UploadedUtc
GroupVisibility   Id, CustomerGroupId, Rule ('IncludeCategory' | 'ExcludeCategory'
                  | 'ExcludeDistributor'), Value
EmailOutbox       Id, SiteId, ToAddress, Template, PayloadJson, QueuedUtc, SentUtc,
                  Attempts, LastError
```

### Column additions

- `Account` — `SiteId`, `VatNumber`, `CompanyRegistrationNumber`, `Website`, `Phone`,
  `InvoicingEmail`, `ShipmentsEmail`, `StatementEmail`, `TaxTreatment`
- `Product` — `Brand` (facet, seeded from `Manufacturer`), `SiteCategoryId`, `Published`,
  `LeadTime`, `Badge`, `Featured`
- `Quote` — `SiteId`, `PoNumber`, `RequestedByContactId`, `Notes`, `Timeline`, `DeliveryRegion`
- `Purchase` — `SiteId`, `PoNumber`, `TaxTreatment`, `BillingAddressId`, `ShippingAddressId`,
  `PaymentStatus`
- `CustomerGroup` — `SiteId`
- `DistributorFeed` — `SiteId`

`SiteId` columns are nullable with a default pointing at the seeded first site, so existing rows
and the WPF POS are unaffected.

### New stored procedures (indicative)

```
spSite_GetByDomain / spSiteTaxRule_GetBySite / spSiteContent_GetBySite
spSiteCategory_GetBySite / spCategoryMapping_GetBySite / spCategoryMapping_Upsert
spCatalog_Search           paged, faceted: category, brand, in-stock, text, sort
spCatalog_GetFacets        counts per category and brand for the current filter
spCatalog_GetBySku         public product detail, respecting group visibility
spContact_Insert / _GetByIdentityUserId / _GetByAccount
spAddress_Upsert / _GetByAccount
spAccount_Register         the whole registration payload in one transaction
spAccountDocument_Insert / _GetByAccount
spGroupVisibility_GetByGroup / _Set
spQuote_SubmitRequest      customer-side submission with lines, one transaction
spQuote_GetByAccount / spOrder_GetByAccount
spEmailOutbox_Enqueue / _DequeueBatch / _MarkSent / _MarkFailed
```

Follow the existing output-parameter convention: insert procedures declare `@Id int output`, the
caller ignores it and re-queries. To return a value from a mutation, `SELECT` it and use
`LoadData<int, dynamic>` — as `QuoteData.DeleteQuoteLine` does.

## 8. Blocking security items

Live defects for a public deployment, not polish. None may ship as they stand, and every future
store inherits whatever is left unfixed.

1. **CORS is fully open.** `StockApi/Program.cs:23` registers `OpenCorsPolicy` with
   `AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()`, applied unconditionally at line 160.
   Restrict to the known client origins — which, with multiple stores, means a per-site origin
   list rather than a single constant.
2. **Swagger is mapped in every environment**, including production. Gate on `IsDevelopment()`
   or require authentication.
3. **JWT signing key comes from configuration.** It is already an Aspire secret parameter locally;
   in production it must come from Key Vault. `KeyVaultFeedSecretStore` shows the pattern.
4. **No rate limiting** on `/token`, registration, or the quote-submission endpoint. Add ASP.NET
   rate limiting plus Identity lockout tuning before exposing them.
5. **No security headers.** HSTS is registered but there is no CSP, `X-Content-Type-Options`,
   or `Referrer-Policy`.
6. **File upload is a new attack surface.** Server-side content-type and size validation,
   randomised storage keys, and no execution from the upload path.
7. **Tenant isolation is a security property, not just a feature.** Once more than one site
   exists, every query that omits a `SiteId` filter is a cross-tenant data leak. Enforce the
   filter in the data layer, and test it.
8. **Card data must never reach this application.** If Stripe lands in v1.1, use hosted checkout
   so the app sees only a session id and a webhook — nothing that would pull it into PCI scope.

## 9. Risks

**Feed taxonomy mapping is the most underestimated item.** The artboards invent six categories and
seven brands. The real feed carries its own category strings and manufacturer names across
thousands of SKUs. Building and maintaining that mapping — including the long tail that fits none
of the six — is data work, and it gates A2. It also recurs for every future store with a different
distributor.

**Product pages will look thin without Icecat.** The `Product.dc.html` artboard renders a
specifications table and a documents list. Nothing in the schema stores either, and the Icecat
licence is deferred, so those sections have no source. Either design a graceful empty state now or
move the licence forward.

**`dbo.Purchase` doing double duty makes order work delicate.** Every new `spOrder_*` procedure must
filter `Reference IS NOT NULL`. A miss writes portal data into POS history and moves
`spReport_GetSales` and `spActivity_GetRecent` with it.

**No test coverage outside the WPF UI tests.** `StockApi`, `SMDataManager.Library` and `SMPortal`
have none. Every refactor in this plan is unguarded. Integration tests around the price resolver,
the tax engine, `spOrder_ConvertFromQuote` and **site scoping** are the highest-value coverage to
write first — those are where a silent wrong answer costs money or leaks data.

**Do not copy the admin portal's data pattern to the storefront.** `AdminDataService` loads the
whole catalog into memory via `EnsureLoadedAsync` and reads synchronously from that snapshot. That
is fine for a handful of staff and fatal for a public page over thousands of SKUs — and worse once
that snapshot would have to be per-site. The storefront queries the database per request, paged.
For the same reason `FuzzySearch` does not move to the storefront; it ranks an in-memory list.

**Template drift.** The failure mode for a platform is a tenant need answered by editing shared
code. Treat "this needs a change in `SMStore` to launch aclitrade" as a signal that a site property
is missing, not as a quick fix.

## 10. Open decisions

- Are list prices public, or is the catalog price-gated behind sign-in? The artboards show public
  list prices with volume pricing on quote — and this may well differ per site, in which case it
  is a `Site` property rather than a global answer.
- Flat group discount only for v1, or per-product overrides? (July design §8 — flat is assumed.)
- Does the distributor offer an on-demand stock check alongside the daily XML? Still open from
  July design §8, and it decides whether quotes can promise stock.
- Stripe in v1 or v1.1? This plan assumes v1.1, invoice terms only at launch.
- Keep or retire the WPF POS? It still writes `dbo.Purchase` and `dbo.Inventory`, still reads the
  `taxRate` app setting, and constrains schema changes.
- Is the admin portal single-tenant or cross-tenant? `SMPortal` currently assumes one world. With
  two sites, staff either pick a site context or see everything — decide before T3.
