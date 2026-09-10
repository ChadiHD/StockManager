# aclitrade.ie — B2B Tech Ecommerce Design

Date: 2026-07-24
Author: Chadi Hammoud (with Claude)

## 1. Background

This document designs the fork of the StockManager codebase into a B2B tech-equipment
ecommerce platform, launching in Ireland under **aclitrade.ie**.

For reference, a third-party vendor quoted a hosted Magento B2B webstore package:

- Webstore Package — £400+VAT/month: dedicated OVH server, SITC import module +
  daily datafeed (Magento 2.4.x), full Icecat licence, SSL, Elastic Search.
- Basic B2B Modules — £100+VAT/month: accounts & contacts, customer groups
  (price/product/distributor), core list, multi-currency, login-as-customer.
- Total: £500+VAT/month, or £5,000+VAT/year with 2 months free on annual payment.

**Decision: build on the existing StockManager fork rather than buy this package.**
The quote is used here only as a feature/cost reference point (see Section 6).

## 2. Scope

### v1 (Ireland launch, aclitrade.ie)

- B2B only — company accounts, not a public consumer storefront.
- Stock from **two sources**, unified in one catalog:
  - Own warehouse stock (existing `InventoryModel`/`ProductModel` in
    `SMDataManager.Library`).
  - Distributor feed stock (new — XML files delivered via SFTP, pulled daily).
- **Quote-based ordering**, not instant cart checkout: customer requests a quote →
  sales/admin prices and confirms it → customer accepts → it becomes an Order.
- Per-account payment mode: card at order confirmation, or invoice/credit terms
  (net-30 style), configurable per account.
- Customer groups with per-group pricing/discount rules and product visibility.
- VAT handling for Ireland (standard 23%, reverse-charge for other EU
  VAT-registered businesses).
- Browser-based admin area for account approval, pricing rules, and order/quote
  management.
- Product search via SQL Server full-text search.

### Deferred (explicitly out of v1)

- Multi-currency, additional countries/domains (architecture allows for it later —
  see Section 5 — but nothing beyond that ships now).
- Icecat rich product content licence.
- Elasticsearch (SQL full-text is enough at this scale).
- EDI / Xero integration, "login as customer" support tooling.
- Public B2C storefront.

## 3. Architecture Overview

Reusing existing projects, extending rather than rewriting:

| Project | Role | Change |
|---|---|---|
| `SMDatabase` | SQL Server schema | Extended with new tables |
| `SMDataManager.Library` | Data access layer | New repositories alongside existing `ProductData`/`InventoryData`/`PurchaseData` |
| `StockApi` | ASP.NET Core + Identity/JWT backend | New controllers for accounts, quotes, orders, pricing |
| `SMPortal` | Blazor WASM storefront | Existing stub `Products`/`Checkout`/`Login`/`Signup` pages become the real B2B site + new admin area |

New components:

1. **Distributor Feed Sync** — scheduled hosted service (in `StockApi` or a
   separate worker under the Aspire `AppHost`) that connects over SFTP, downloads
   the daily XML feed, and syncs it into the database (see Section 4).
2. **Pricing / Customer Group engine** — resolves price per (product, account)
   from group discount rules and margin-over-cost rules.
3. **Account & Quote/Order module** — company accounts with multiple contacts,
   admin approval workflow, quote lifecycle, and order records with the
   per-account payment mode.
4. **Browser-based admin** — new admin area (in `SMPortal` or a dedicated admin
   app) for account approval, pricing rules, quote handling, and order
   management. Chosen over extending the WPF desktop app (`SMDesktopUI`) so
   staff aren't tied to one machine.

## 4. Data Model Changes

New/changed entities in `SMDataManager.Library` + `SMDatabase`:

- **Account** — company name, VAT number, billing/shipping addresses,
  `PaymentMode` (Card/CreditTerms), `CreditLimit`, `PaymentTermsDays`,
  `Status` (Pending/Approved/Suspended), `CustomerGroupId`, `SiteId`.
- **Contact** — extends `ApplicationUserModel`/`UserModel` with an `AccountId`
  link and a role within the account (Admin/Buyer).
- **CustomerGroup** — discount/margin rule plus product-visibility rules
  (some distributor lines may be hidden from some groups).
- **DistributorProduct** / **DistributorStock** — new tables holding feed data
  (SKU, cost, list price, description, qty, `LastSyncedAt`), kept separate
  from the existing `InventoryModel` (own warehouse stock, unchanged).
- **CatalogItem** (aggregation, not necessarily its own table) — unifies
  `InventoryModel` and `DistributorProduct` into one product listing, tagged
  with `StockSource = Own | Distributor`.
- **Quote** — Requested → Priced → Accepted/Rejected states, account/contact
  reference, lines referencing either stock source.
- **Order** — created on quote acceptance; payment-mode snapshot, VAT
  treatment (standard vs. reverse-charge), lines.

Existing `ProductModel` and `PurchaseModel`/`PurchaseDBModel` are unchanged —
they continue to serve internal purchasing/stock-receipt workflows.

## 5. Distributor Feed Integration

- **`IDistributorFeedClient`** interface in `SMDataManager.Library`, with a
  concrete `SftpXmlFeedClient` implementation (using SSH.NET) that connects to
  the feed's host/port with username/password and downloads the daily XML
  file(s).
- **Staging → upsert**: the parsed XML lands in a staging table, then upserts
  into `DistributorProduct`/`DistributorStock`. Discontinued SKUs are flagged,
  not hard-deleted, so historical quotes/orders still resolve.
- **Freshness**: since this is a daily file drop rather than a live query API,
  stock quantities are only as fresh as the last sync. Quotes should note
  "stock subject to distributor confirmation" for feed-sourced items unless
  the distributor also offers an on-demand stock check to call at
  quote-acceptance time (worth asking the distributor about).
- **Failure handling**: each item carries `LastSyncedAt`; if a sync fails or
  data goes stale past a threshold, admin is notified and stale items can
  auto-hide from new quotes (configurable).
- Feed credentials live in configuration/secrets, not committed to the repo.

## 6. Multi-Country Expansion Architecture (future-proofing only)

Not built in v1, but one cheap addition now avoids a costly rewrite later:

- New **`Site`** entity: `Domain`, `Country`, `CurrencyCode`, `VatRules`, and
  its own `DistributorFeedConfig` (different countries will likely use
  different distributors).
- Every scoped entity (`Account`, `CustomerGroup`, `DistributorProduct`/
  `DistributorStock`, `Quote`, `Order`) carries a `SiteId` column now, even
  though only one `Site` row (Ireland) exists at launch.
- The distributor sync worker loops over active `Site` records — adding a
  second country later means adding a `Site` row + feed credentials, not new
  sync code.
- **Single shared database**, scoped by `SiteId` — not separate databases per
  country. Simpler to operate; can be split later if scale ever demands it.
- Backend: one `StockApi` deployment can serve multiple domains, resolving
  `Site` from the request host. Frontend: `SMPortal` can be deployed once per
  domain pointed at the shared API.

## 7. Hosting & Cost Comparison

Rough self-hosted infra costs (EU/Ireland region), separate from dev time:

| Item | Estimate |
|---|---|
| App hosting (Azure App Service small tier, or an OVH VPS) | ~€20–40/month |
| Database (Azure SQL Database) | ~€5/month at the smallest tier, up to ~€15–75/month for a tier with real backup retention/performance |
| SSL | Free (Let's Encrypt or bundled with host) |
| Domain (aclitrade.ie) | Promotional first year often €3–25; typical renewal €20–50/year. **Note:** .ie domains require IEDR identity/connection verification before going live — start this early so it isn't a launch blocker |
| Elasticsearch / Icecat | €0 (deferred) |

**All-in estimate: ~€50–150/month.**

Vendor quote comparison: £500+VAT/month (≈€700+/month), or £5,000+VAT/year with
the annual discount (≈€594/month average). Self-build is roughly **4–10x
cheaper** on pure hosting.

Honest trade-off: the vendor fee isn't just hosting — it bundles a maintained
Magento platform, a pre-built SITC-style integration, Icecat content, and
implicit vendor support/patching. Self-build trades that recurring fee for
upfront dev time, plus owning ongoing ops (patching, backups, uptime,
security) directly. Given the existing StockManager codebase already covers
much of the foundation, this trade favors building — but it is genuinely more
of your own time, not just less money.

## 8. Open Items / Next Steps

- Confirm whether the distributor feed offers an on-demand stock check API in
  addition to the daily XML pull (affects quote accuracy).
- Decide CustomerGroup pricing model detail (flat discount % vs. per-product
  overrides) during implementation — flat discount is the simpler v1 default.
- Register aclitrade.ie early given .ie verification lead time.
- Confirm VAT reverse-charge handling with an accountant/tax advisor before
  go-live (this document is not tax advice).
