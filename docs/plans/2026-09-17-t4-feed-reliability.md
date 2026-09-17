# T4 — Feed reliability

Detail plan for the fourth template phase. Sits under
`docs/plans/2026-09-10-storefront-implementation-plan.md` §T4 and replaces its bullet list with
something buildable. T0–T3 are merged; this is the next phase and nothing in the tenant track
starts until the template track finishes.

**Exit:** feeds sync nightly unattended; a failed sync is visible and does not poison the
catalog.

**Status: all six items built.** The claim, the sync history, the nightly scheduler, staleness
hiding, alerting and the delisted-SKU verification are all in. What this plan said would
not be in T4 still is not: the quote cart and what an accepted quote does with a delisted line
(T5), on-demand stock confirmation, per-feed schedules and retries, and the email outbox (T6).

Most of T4 is finishing what exists rather than writing something new. `DistributorFeedSyncService`
already fetches, parses, upserts and delists correctly, and `spProduct_BulkUpsertFromFeed` is
already transactional. What is missing is everything around it: nothing runs it but a button, a
failure is a status string nobody is looking at, and a feed that silently stopped delivering
leaves month-old stock quantities on the storefront looking exactly like fresh ones.

---

## 1. The decision that comes first: what stops two syncs

A scheduled sync introduces a problem the manual button never had — more than one of them.

Three ways that happens, and only the third is speculative:

- **Two operators press Sync.** Possible today. Both fetch, both upsert. Not corrupting,
  because each import builds its whole `@Items` set before the MERGE and the procedure is
  transactional, but it is two SFTP sessions and two full imports for one result.
- **A scheduled run overlaps a manual one.** New with T4, and the likely case: an operator
  presses Sync at 02:00:30 because the nightly run looks stuck.
- **Two `StockApi` instances.** `.github/copilot-instructions.md` keeps the Aspire setup
  publishable as Azure Container Apps, where the replica count is a slider. Two replicas both
  hold a `BackgroundService`, both wake at the configured hour, and neither knows about the
  other.

### Decision: claim the feed row, do not lock the application

`spDistributorFeed_ClaimForSync` sets `SyncStartedUtc` on a feed where it is NULL or older than
a lease, and reports whether the claim was taken. A caller that does not get the claim does not
sync, and says so. `spDistributorFeed_RecordSync` clears it.

Considered and rejected: `sp_getapplock`. It is the textbook answer and it needs the lock held
on one open connection for the whole sync, which `ISqlDataAccess` does not offer — every call
opens and closes. Faking it with a held `IDbConnection` would mean a second data-access shape
in the library for one caller's benefit.

The lease matters more than the lock. A process killed mid-sync leaves `SyncStartedUtc` set
forever, and a feed that can never be claimed again is a worse failure than a double import —
it is silent, where a double import is merely wasteful. So the claim is `NULL OR older than
Feeds:ClaimLeaseMinutes`, defaulting to an hour: longer than any real sync, short enough that
the next night recovers on its own.

**Consequence to build:** a claim refused is not an error. `DistributorFeedResult` needs to say
"already running" distinctly from "failed", or the portal will show a red banner every time
somebody presses a button during the nightly window.

---

## 2. What "stale" means, and what it is allowed to hide

The design (§5 of the July document) asks for two things: an admin notified when data goes
stale past a threshold, and stale items that "can auto-hide from new quotes (configurable)".

### Stale is per site, because the threshold is a commercial judgement

A distributor dropping a file every night makes 26 hours old suspicious. One delivering twice a
week makes it normal. That is a property of the store's relationship with its distributor, so it
belongs on `Site` alongside `MinMarginPct` and `PriceDisplay` — not in `appsettings.json`, which
would apply one number to every tenant.

Two columns:

- `Site.FeedStaleAfterHours` — how old `Product.LastSynced` may be before a distributor-sourced
  product counts as stale. Zero disables staleness entirely.
- `Site.HideStaleProducts` — whether a stale product disappears from the storefront, or merely
  shows up in the operator's alerts.

Both default to off, so this lands inert and a store turns it on deliberately. A platform-wide
default that started hiding products on upgrade would be the wrong way round.

### The predicate goes in `fnCatalog_VisibleProducts`, and has to arrive pre-resolved

That function is inline for the reasons its own comment gives, which means it cannot `DECLARE`.
So the caller resolves the cutoff and passes `@StaleBeforeUtc`, exactly as it already does for
`@DiscountPct` and `@MinMarginPct`. NULL means no hiding.

Two conditions on that predicate, both easy to get wrong:

- **`Source <> 'Distributor'` is never stale.** A product the store owns has no `LastSynced` at
  all, and a naive `LastSynced >= @cutoff` would hide the entire own-brand catalog the first
  time a store set the threshold.
- **`spCatalog_GetFacets` must pass the same cutoff.** The whole reason
  `fnCatalog_VisibleProducts` exists is that the search and the facet counts have to agree
  exactly; a cutoff passed to one and not the other reintroduces the bug the function was
  extracted to kill.

`CatalogPriceParityTests` calls this function directly and will need the new argument. That is
the tripwire working as designed — a signature change that the parity test did not notice would
mean the test had stopped exercising the real thing.

### Hiding from the storefront, not from quotes

The design says "from new quotes"; the implementation plan says "from the storefront". The
storefront reading is stronger and is what T4 builds: a stale product is not offered, so it
cannot reach a quote in the first place. Quote lines already in existence are untouched — see
§4.6.

---

## 3. Schema

**New table.**

- `dbo.DistributorFeedSyncLog` — `Id`, `FeedId`, `SiteId`, `StartedUtc`, `FinishedUtc`,
  `Succeeded`, `RecordCount`, `Imported`, `Delisted`, `Message`, `TriggeredBy`.
  `LastSyncStatus` holds one 400-character string and is overwritten by the next run, so today
  a feed that failed every night for a week looks exactly like one that failed once. This is
  the history that makes "it has been broken since Tuesday" answerable.
  - `TriggeredBy` is `'Schedule'` or `'Operator'`. Worth a column: a feed that only ever
    succeeds when somebody presses the button is a scheduler problem, not a feed problem, and
    nothing else would distinguish the two.
  - Scoped by site because a feed is, and because this table quotes error messages that can
    name a distributor's hostname.
  - No pruning. A nightly sync of four feeds writes about 1,500 rows a year.

**Column additions.**

- `dbo.DistributorFeed.SyncStartedUtc` — the claim from §1. NULL means not running.
- `dbo.Site.FeedStaleAfterHours` `INT NOT NULL DEFAULT 0` — §2.
- `dbo.Site.HideStaleProducts` `BIT NOT NULL DEFAULT 0` — §2.
- `dbo.Site.OperatorEmail` `NVARCHAR(320) NULL` — where a failure alert goes. Per site,
  because the back office is per store; NULL means log only, and nothing invents an address.

**New procedures.**

- `spDistributorFeed_ClaimForSync`, and `spDistributorFeed_RecordSync` extended to release the
  claim and write the log row in one call.
- `spDistributorFeedSync_GetByFeed`, `spDistributorFeedSync_GetRecent` — the history the portal
  shows.
- `spDistributorFeed_GetStale` — feeds whose `LastSyncedUtc` is older than their site's
  threshold, for the alerting pass and the portal banner.

**Removals.**

- `spProduct_SyncFeeds`, `IProductData.SyncDistributorFeeds` and `ProductData.SyncDistributorFeeds`.
  The procedure stamps `LastSynced = SYSUTCDATETIME()` on every distributor-sourced product
  **without fetching anything** — it was the placeholder before a real feed client existed, and
  nothing has called it since `Catalog/Sync` became real. Left in place it is a loaded gun: once
  §2 ships, calling it marks the entire catalog fresh and un-hides every stale product while
  making the data no newer.

---

## 4. Work items, in order

**1. The claim.** `SyncStartedUtc`, `spDistributorFeed_ClaimForSync`, the lease, and
`DistributorFeedResult` learning to say "already running". First, because everything after it
runs concurrently with something.

**2. Sync history.** `DistributorFeedSyncLog` and the procedures, written from
`DistributorFeedSyncService.RunFeedAsync` where the numbers already exist. Nothing new is
measured; what changes is that the measurements are kept.

**3. The scheduler.** `DistributorFeedSyncBackgroundService` in `StockApi`, next to
`ProductImageBackgroundService` and for the same reason — `StockApi` holds the Data Protection
ring that decrypts `SecretRef`. It loops over active sites and calls `SyncAllAsync` for each,
per the July design's §6 promise that a second country is a `Site` row rather than new sync
code.
   - Off by default (`Feeds:SyncEnabled`), like `Icecat:EnrichmentEnabled`. A developer running
     the app host should not open an SFTP session to a distributor because they pressed F5.
   - One configured time of day rather than a cron expression or a per-feed schedule. A missed
     run waits for the next night, which the staleness alert is there to surface; per-feed
     timing becomes a `DistributorFeed` column when a second distributor actually wants one.

**4. Staleness.** The two `Site` columns, `@StaleBeforeUtc` through `fnCatalog_VisibleProducts`
and both its callers, and the parity test updated.

**5. Alerting.** `spDistributorFeed_GetStale`, a failure alert through the existing
`IEmailSender` seam to `Site.OperatorEmail`, and a banner on `/admin/feeds` showing failed and
stale feeds with their history. The mail is the push half and the banner is the pull half;
neither alone satisfies "a failed sync is visible", because nobody watches a log and a banner
nobody opens is not a notification.
   - Alert on transition, not on every run. A feed broken for a week must not send seven
     identical mails, or the eighth will be filtered. The log table from item 2 is what makes
     "did the last run also fail" answerable.

**6. Delisted-SKU verification.** A test, not a feature: `spQuoteLine_GetByQuote` joins
`Product` with no `Delisted` predicate, so a quoted product that the distributor later dropped
still resolves — but nothing asserts that, and it is one well-meant `AND p.Delisted = 0` away
from historical quotes rendering blank lines. Also verify the staleness predicate from item 4
cannot reach a quote line the same way.

---

## 5. Operational requirements

- **A sync must never leave the catalog half-imported.** Already true —
  `spProduct_BulkUpsertFromFeed` is one transaction — and the reason the scheduler may call it
  unattended at all. Anything added to the import path keeps that property or does not go in it.
- **A failed sync must not delist anything.** Also already true, because the delist pass is
  inside the same transaction as the upsert and an exception rolls both back. Worth stating
  because the tempting optimisation — delist first, then import — would empty a store's catalog
  the first time an SFTP session dropped mid-transfer.
- **Feed credentials stay where they are.** `SecretRef` is ciphertext bound to the Data
  Protection ring; the scheduler resolves it through the same `IFeedSecretStoreResolver` the
  controller uses and never logs the result. See CLAUDE.md on moving the key ring.
- **An error message from a feed goes to the log table and the portal, not to a customer.**
  Exception text can carry a hostname, a path and a username. `RunFeedAsync` already trims to
  the message rather than the whole exception; the log row keeps that limit.
- **The alert mail says a feed failed, not why in detail.** It goes to a configured address
  which is trusted, but it also passes through `LoggingEmailSender`, which outside Development
  no longer logs bodies — so the diagnosis lives in the portal and the mail is the pointer.

---

## 6. Not in T4

- The quote cart and quote submission — T5. Item 4's storefront hiding stops a stale product
  reaching a cart; what an *accepted* quote does with a delisted line is T5's decision.
- On-demand stock confirmation at quote acceptance. The July design raises it as a question for
  the distributor and it needs an API this feed does not have.
- Per-feed schedules, cron expressions, and a retry policy for a failed nightly run.
- The email outbox and templates — T6. T4 adds a call site to the existing seam, nothing more.
- Product images. `ProductImageBackgroundService` and the 3.8% Icecat coverage are unchanged;
  images remain a sourcing problem rather than a code problem.

---

## 7. Risks

- **A store turns on staleness hiding with a threshold that is too tight** and half its catalog
  vanishes at once. Mitigated by defaults — both columns start off — and by own stock being
  exempt. **Not** mitigated by a preview: this plan said the portal would show how many
  products a threshold would hide before it was saved, and it does not, because `Site` has no
  admin screen at all. These columns are set by updating the row, so today the person changing
  them has database access and can count the rows themselves. A site settings screen is the
  real fix and belongs to whoever builds one.
- **The scheduler runs in a host that is scaled to zero.** Azure Container Apps can scale an
  app to no replicas, and a `BackgroundService` in no replica does not run. Out of scope to
  solve, in scope to write down: T7 owns the hosting decision, and this is one of its inputs.
- **`fnCatalog_VisibleProducts` gains a parameter**, which is a signature every caller and the
  parity test must follow. That is three call sites today and the function exists precisely so
  it stays three.
