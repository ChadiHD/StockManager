# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 10 stock-management solution being built into a **reusable B2B ecommerce platform** — a
template from which multiple storefronts can be stood up, not a one-off site. aclitrade.ie
(Ireland) is the first store to launch on it and the driver of current requirements, but it is
one tenant among future ones, so prefer the generic mechanism over the Ireland-specific shortcut:

- Site-scoped data over hardcoded assumptions. Country, currency, VAT rules, domain and
  distributor feed config belong to a `Site` row, not to constants.
- Branding, catalog taxonomy and content are configuration or per-site assets, never baked into
  components.
- Anything named or worded for aclitrade specifically belongs in that site's configuration or
  content, not in shared code.

Two documents drive the work — read both before adding features to the admin portal, storefront,
quotes, accounts, customer groups or distributor feeds:

- `docs/plans/2026-07-24-aclitrade-b2b-ecommerce-design.md` — the target design
- `docs/plans/2026-09-10-storefront-implementation-plan.md` — the build plan and current state

**The template is finished before the first tenant is built.** The plan runs two tracks, T0–T7
for the platform and A0–A4 for aclitrade.ie, and the platform track goes first in full. A
tenant built alongside an unfinished template is how store-specific assumptions get into shared
code, and the whole point of this exercise is that store number two costs days rather than
months. T0, T1 and T2 are done and merged; T3 is next.

A corollary worth taking literally: **if a tenant task requires editing shared code, that is a
template gap.** Fix the template and let the tenant consume it, rather than special-casing.

.NET Aspire orchestrates everything.

| Project | Role |
| --- | --- |
| `StockManager.AppHost` | Aspire orchestrator. **The entry point** — dashboard on 17291 |
| `StockApi` | ASP.NET Core Web API, HTTPS **pinned to 7042** (clients hardcode it) |
| `SMDataManager.Library` | Dapper + stored-procedure data access. Shared by `StockApi` and `SMStore` |
| `SMDatabase` | SDK-style `.sqlproj` (`Microsoft.Build.Sql`) — tables, procedures, functions |
| `SMStore` | Blazor **Web App** (SSR) customer storefront. Serves every site from one deployment |
| `SMPortal` | Blazor **WebAssembly** admin portal (`/admin/*`) |
| `SMDesktopUI` + `.Library` | WPF POS desktop app (legacy, still shipped) |
| `SMDesktopUI.UITests` | xunit + FlaUI UI automation for the WPF app |
| `StockManager.ServiceDefaults` | Shared Aspire telemetry, health checks, resilience, Data Protection |

`SMStore` does **not** call `StockApi`. It renders on the server and reads `SMDatabase` through
`SMDataManager.Library` in process — an HTTP hop to a co-located API would only add a token
exchange to every anonymous catalog page. `SMPortal`, being WebAssembly, still goes through the
API.

## Multi-store rules

Every request resolves to a `dbo.Site` row from its host header, via `SiteResolutionMiddleware`,
before anything else runs. Consequences that are easy to get wrong:

- **Read site values from `ISiteContext`, never from configuration or a constant.** Country,
  currency, locale, ordering mode, registration field set and price visibility all live on the
  site.
- **An unresolved host is a 404, never a fallback to a default store.** Falling back is how one
  tenant's catalog and prices get served on another tenant's domain.
- **Every query over a scoped entity filters on `SiteId`.** Omitting it is a cross-tenant data
  leak, not a display bug. Scoped entities: `Account`, `CustomerGroup`, `Quote`, `Purchase`
  (portal orders only), `DistributorFeed`, `SiteCategory`, `CategoryMapping`, `SiteContent`.
- **A join to a scoped table needs the predicate even when the join path looks safe.** A
  `CategoryMapping` row scoped to site A can name a `SiteCategory` belonging to site B — the
  mapping's own `SiteId` says nothing about the category it points at. The catalog procedures
  filter `SiteCategory` explicitly, and `FK_CategoryMapping_ToSiteCategory` is composite
  (`SiteCategoryId, SiteId`) so the database refuses the mismatched row in the first place.
  Prefer the composite key wherever a scoped row references another scoped row.
- **Nothing outside `SMStore/Ordering/` branches on `Site.OrderMode`.** Ask
  `OrderingModeProvider.Current` instead.
- **`wwwroot/app.css` holds no colour of its own.** It reads custom properties that a theme
  under `wwwroot/sites/{SiteKey}/` defines. `SiteThemeResolver` falls back to the `default`
  theme for any asset a site is missing.
- `Sites:ForceSiteKey` pins every request to one store for local work. `SMStore` refuses to
  start with it set outside Development.

### Scoping is done on writes and not on admin reads

**Read scoping in the admin API is not implemented, and the storefront's is.** `SMStore`
resolves a site per request and every storefront query filters on it. `StockApi` and `SMPortal`
have no site concept at all: no controller, service or data class mentions `SiteId`, and
`spAccount_GetAll`, `spQuote_GetAll`, `spOrder_GetAll`, `spCustomerGroup_GetAll` and
`spDistributorFeed_GetAll` return every store's rows. That is correct only while an admin is
global. It stops being correct the moment an admin belongs to one store — and
`spDistributorFeed_GetAll` returning every tenant's `SecretRef` is the sharpest edge of it,
since `StockApi` holds the key ring that decrypts them. **Deciding whether an admin is global
or per-site is a prerequisite for a second tenant, not a later refinement.**

The write half is done. Every insert over a scoped entity sets `SiteId`: `spAccount_Insert`,
`spCustomerGroup_Insert` and `spDistributorFeed_Insert` take an optional `@SiteId`, while
`spQuote_Insert`, `spOrder_Insert` and `spOrder_ConvertFromQuote` derive it from the account or
quote the row descends from. All six resolve through `dbo.fnSite_Resolve`, which fills in the
only site when a database has exactly one and returns NULL when it has more, so the procedure
throws rather than guessing. This matters more than it looks: `UQ_CustomerGroup_Name`,
`UQ_CustomerGroup_Slug` and `UQ_DistributorFeed_Name` are scoped by site, and SQL Server treats
NULL as one distinct value in a unique constraint — while `SiteId` went unset, the second store
to want a "Reseller" group or a "Main" feed simply could not be created.

Data Protection keys are shared by `StockApi` and `SMStore` through `AddSharedDataProtection`,
persisted to `dbo.DataProtectionKeys` in `ApiAuthDb`. Both apps must keep the same application
name, or neither can read the other's cookies or the feed credentials in `dbo.DistributorFeed`.

**Moving the key ring orphans everything already encrypted against the old one.** When
`AddSharedDataProtection` replaced the previous per-machine ring
(`%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`), the new database-backed ring started empty, so
the distributor feed password stored months earlier could no longer be decrypted —
`The key {guid} was not found in the key ring`, surfacing as a 502 on sync. There is no
recovery but re-entering the password on the feed, which re-encrypts against the current ring;
`DataProtectionFeedSecretStore.ResolveAsync` now says exactly that instead of surfacing the raw
cryptographic error. Before changing where keys live again, remember that every `SecretRef` in
`dbo.DistributorFeed` is ciphertext bound to the ring that wrote it.

## Build and run

Run everything through the app host:

```bash
dotnet run --project StockManager.AppHost/StockManager.AppHost.csproj
```

Aspire provisions SQL Server as a **persistent container** (`AddAzureSqlServer("sql").RunAsContainer()`
with volume `stockmanager-sql-data`) holding `ApiAuthDb` and `SMDatabase`. The connection strings in
`StockApi/appsettings.json` are overridden by Aspire at run time — the `SITIHAPIB` value there is not
what a running app uses.

### SMDatabase builds with the dotnet CLI

`SMDatabase.sqlproj` is an SDK-style project (`Microsoft.Build.Sql/2.2.0`), so the DACPAC comes
from a plain build and `dotnet build StockManager.sln` works:

```bash
dotnet build SMDatabase/SMDatabase.sqlproj
```

It was classic SSDT until 2026-09-10 and `dotnet build` failed with `MSB4278`. An earlier
conversion attempt on Microsoft.Build.Sql 1.x was reverted because VS 18's SSDT targets pass
`SqlBuildTask` parameters the 1.x task did not declare (`MSB4064` / `MSB4063` on
`AutomaticIndexCompaction` or `AcceleratedDatabaseRecovery`). The 2.x SDK declares them. **If a
Visual Studio build ever reports `MSB4064` on a property name again, that is this collision
returning — look at the SDK version, not the project layout.**

`<TargetFramework>netstandard2.0</TargetFramework>` is pinned in the sqlproj and must stay.
Left unset, the two toolchains disagree: `dotnet build` restores `netstandard2.0` while a build
through Visual Studio evaluates `net472`, because VS still supplies the legacy SSDT
`TargetFrameworkVersion` of `v4.7.2`. The result is
`error NETSDK1005: Assets file ... doesn't have a target for 'net472'`. With it pinned, both
toolchains share one `obj/project.assets.json` and can be alternated without a re-restore.

`StockManager.AppHost` carries a build-only `ProjectReference` to the sqlproj
(`ReferenceOutputAssembly="false" IsAspireProjectResource="false"`), so building the app host
builds the schema too and there is no separate step after touching SQL — just rebuild and restart.

**The app host does not guess where the DACPAC is.** Its `ResolveSMDatabaseDacpacPath` target asks
the SQL project (`GetTargetPath`) and stamps the answer in as `AssemblyMetadata`, which `AppHost.cs`
reads back. The path moves with `$(Configuration)` and again with `$(BaseOutputPath)`, so a
hardcoded `bin\Debug` sent a Release or output-redirected build's guard at a file nothing had
written. Keep the target and the project reference together — `AppHost.cs` throws with an
actionable message if the metadata is absent.

It still **refuses to start** if that DACPAC is missing or older than any `.sql` file under
`SMDatabase/`, which catches a hand-edited `.sql` that was never rebuilt. `smdatabase-schema`
publishes it automatically (`WithSkipWhenDeployed`, so an unchanged DACPAC is skipped).
`AppendTargetFrameworkToOutputPath` is off in the sqlproj so the output does not nest under a
target-framework folder.

Building `StockManager.AppHost.csproj` **alone** with Visual Studio's MSBuild from a clean `obj`
fails with `CS0246: The type or namespace name 'Projects' could not be found` — the Aspire SDK
generates those classes from the reference graph. Build the solution, or use `dotnet build`.

### Locked bin directories

While the app host, `StockApi.exe` or `devenv.exe` are running, builds fail with `MSB3027` /
`MSB3021` copy errors — the running processes hold the output DLLs. Do not kill the user's processes.
Compile-check into a scratch directory instead:

```bash
dotnet build StockApi/StockApi.csproj -p:BaseOutputPath=<scratch-dir>/bo/ -v q --nologo
```

(`-p:OutputPath=` for the sqlproj.) Compilation errors still surface; only the copy step is skipped.

### Tests

```bash
dotnet test SMDesktopUI.UITests/SMDesktopUI.UITests.csproj
dotnet test SMDesktopUI.UITests/SMDesktopUI.UITests.csproj --filter "FullyQualifiedName~MyTest"
```

FlaUI drives a real WPF window, so these need an interactive desktop session. The app host registers
them as `desktop-ui-tests` with `WithExplicitStart()` — they run on demand from the dashboard, never
on launch.

`SMDataManager.Library.Tests` holds exactly one thing: a parity check between the net-price
expression in `dbo.fnCatalog_VisibleProducts` and `PriceResolver`. The catalog sorts on the
SQL copy and displays the C# one, so this is the tripwire that makes that duplication safe.

It needs a database — evaluating the SQL half has no other way, and a C# reimplementation
would be a third copy of the thing under test. Point `SMDATABASE_TEST_CONNECTION` at a
development database (the connection string is on the `sql` resource in the Aspire dashboard)
and note the host: **use `127.0.0.1`, not `localhost`** — the container publishes on IPv4 only
and `localhost` resolves to `::1` first, which fails as a connect timeout rather than
anything legible.

```bash
SMDATABASE_TEST_CONNECTION="Server=127.0.0.1,<port>;Database=SMDatabase;User Id=sa;Password=<pw>;TrustServerCertificate=True;Encrypt=False" \
  dotnet test SMDataManager.Library.Tests/SMDataManager.Library.Tests.csproj
```

Without that variable the tests skip rather than fail. Everything they write happens inside a
transaction that is never committed.

There are no tests for `StockApi`, `SMStore` or `SMPortal`.

`.claude/launch.json` has entries for `preview_start`. `sm-portal-standalone` runs the portal alone on
7250, which avoids fighting the app host for ports when iterating on UI.

## Data access

Every business query goes through `ISqlDataAccess`, naming a stored procedure and the connection
string `"SMDatabase"`. There is no EF for business data (the `dotnet-ef` tool exists only for ASP.NET
Identity).

```csharp
_sqlDataAccess.LoadData<QuoteModel, dynamic>("dbo.spQuote_GetByReference", new { Reference = reference }, "SMDatabase");
_sqlDataAccess.SaveData("dbo.spQuote_UpdateStatus", new { Id = quoteId, Status = status }, "SMDatabase");
```

**Output parameters are declared in the sprocs but never read.** `SaveData` passes an anonymous
object, which Dapper cannot write back through. Insert procedures therefore take `@Id int output`,
ignore it, and the caller re-queries for the newest row. To return a value from a mutation, `SELECT`
it and call `LoadData<int, dynamic>` instead — see `QuoteData.DeleteQuoteLine`.

Adding a stored procedure is one step: create the `.sql` file under
`SMDatabase/dbo/Store Procedures/` (or `SMDatabase/dbo/Functions/`, which holds
`fnSite_Resolve`). The SDK-style project globs `**/*.sql`, so there is no longer a
`<Build Include="..." />` list to keep in step — that was the old classic-SSDT footgun, where a
file left out of the project silently never deployed.

**The glob replaced that footgun with a quieter one: Visual Studio evaluates it when it loads the
project and does not rescan.** A `.sql` file created outside the IDE — by an agent, a git pull, a
branch switch — is missing from VS's item list, so a build there produces a DACPAC without it and
the app host deploys schema that silently lacks the new object. The build succeeds and the
staleness guard passes, because the DACPAC *is* newer than the sources. The symptom is a runtime
`Invalid object name` or `Could not find stored procedure` for something that plainly exists on
disk. Reload the project (or build with `dotnet build`, which evaluates the glob fresh) and
confirm before blaming the deploy:

```bash
dotnet build SMDatabase/SMDatabase.sqlproj && \
  unzip -p SMDatabase/bin/Debug/SMDatabase.dacpac model.xml | grep -c YourNewObject
```

`Scripts/PostDeployment/Seed.sql` runs on **every** publish, so everything in it must be
idempotent. Its job is to guarantee a `dbo.Site` row exists — multi-store scoping means a
database with no site renders nothing — and to backfill `SiteId` on rows that predate it. The
site it seeds is deliberately generic; a real store is inserted as tenant configuration, not
baked into the platform schema.

### `dbo.Purchase` does double duty

`Reference IS NULL` is a desktop POS sale. `Reference IS NOT NULL` is a portal sales order, and only
then are `AccountId`, `QuoteId`, `Currency` and `Status` populated. Every `spOrder_*` procedure filters
on `Reference IS NOT NULL` to keep the two apart — preserve that predicate in anything new, or admin
writes will reach POS history.

Portal orders never touch `dbo.Inventory`; only `spInventory_Insert` writes it. Order-level changes
therefore have no stock side effects, but they do move `spReport_GetSales` and `spActivity_GetRecent`.

## Auth

`StockApi` runs two schemes and picks per request: a bearer token means JWT, no bearer token means the
Identity application cookie for the Razor UI. `SMPortal` and the WPF app `POST /token`
(`TokenController`) and hold the JWT in local storage under the key from
`SMPortal/wwwroot/appsettings.json`. Admin API controllers are `[Authorize(Roles = "Admin")]`.

`AdminDataService.EnsureAuthHeaderAsync` reads the token straight from storage rather than trusting
`AuthStateProvider` to have set it — on a full page reload the service can run first, and every call
401s otherwise.

## SMStore architecture

Static SSR by default. There is no interactive render mode on any page yet, and adding one
should be a deliberate decision about a specific island rather than a reflex — the mobile menu
is a CSS-only disclosure (hidden checkbox plus a sibling selector) precisely to avoid a circuit
on every page.

The design system came from the Template artboards' `_ds/styles.css` and is split in two:

- **`wwwroot/app.css`** holds the system — blueprint frame, `.btn`, `.card`, `.input`, `.tag`,
  `.table`, `.dialog`, the type scale, `.container` / `.page`, `.breadcrumb`, `.product-grid`,
  `.stub`. It defines **no** colour, space, radius or font. A token added here must be added to
  every theme.
- **`wwwroot/sites/{SiteKey}/theme.css`** holds the `:root` token block and the webfont import.
  A palette and a typeface are brand, not platform.

Component-specific rules live in scoped `.razor.css`. A class used by more than one component
belongs in `app.css` instead — scoped CSS does not reach another component's markup, which is
why `.product-grid` is global. `::deep` reaches markup a child renders, as `FormField` needs to
style the caller's own `.input`.

Themes supply `theme.css`, `logo.svg`, `logo-inverse.svg` and `favicon.svg`.
`SiteThemeResolver` falls back to the `default` theme per missing file. The inverse logo exists
because the footer is painted in `--color-accent-900` and a dark mark disappears into it.

`/_design` renders every primitive against the current theme. Development only — it 404s
elsewhere — and it is the quickest way to see whether a token or component change broke
something.

Two conventions worth keeping:

- **Unfinished actions disable themselves.** `ProductCard`'s add button binds an
  `EventCallback` and disables when nothing is wired, so later phases supply a handler rather
  than replacing markup. Prefer that to a button that silently does nothing.
- **Nothing store-specific belongs in a component.** Navigation is assembled in
  `StoreNavigation`; editorial copy resolves through `ISiteContentSource` from
  `dbo.SiteContent`, and a store with no row gets an explicit empty state rather than another
  store's words. `SiteContent.BodyHtml` renders as `MarkupString`, so rows are staff-authored
  only — nothing originating with a customer may reach that column.

The basket page carries both `@page "/quote"` and `@page "/cart"`; which one a store links to
comes from `OrderingModeProvider`, so neither route 404s and no markup branches on `OrderMode`.

### Catalog and pricing

Pages never touch `ICatalogData` or `IPriceResolver`. Both go through `CatalogPresenter`,
because resolving a price and deciding whether to show one at all are the two things most
easily got subtly wrong in a page.

`spCatalog_Search` decides visibility inside the query rather than filtering afterwards — a
product removed from an already-fetched page has still been counted, still shifted the paging,
and has usually already reached the browser. Ranking is a literal ladder (exact SKU, exact MPN,
prefix, contains, description) because the dev SQL Server reports `IsFullTextInstalled = 0`, so
`CONTAINSTABLE` is unavailable. It is deliberately confined to that one procedure, so adopting
full text later is a change in one place.

Three hazards in the query path, all of which have already bitten:

- **A query-string parameter bound as `int?` is a 500 waiting to happen.** Blazor converts the
  value during binding and throws on anything it cannot parse, before any of your validation
  runs — `?page=abc`, or any number past `int.MaxValue`. Bind as `string` and parse. `Catalog`
  is the only page with a numeric query parameter; keep it that way.
- **Clamp page numbers in the procedure as well.** `(@Page - 1) * @PageSize` is int arithmetic,
  and an unbounded page number overflows it into an unauthenticated 500.
- **An empty page carries no total.** The count rides on the rows via `COUNT(*) OVER ()`, so a
  page past the end reports zero matches for a category that is full and drops the pager
  entirely. `CatalogPresenter` re-queries to recover the total and lands on the last page.

Two known-wrong things that are invisible only because sign-in does not exist yet, and that T3
turns on:

- `CatalogPresenter.CustomerGroupId` is hardcoded null, so `groupDiscountPct` is always 0 and
  the margin floor in `PriceResolver` can never bind.
- Because of that, `ORDER BY RetailPrice` and the displayed net price agree. **They stop
  agreeing the moment a real group discount exists and any site sets `MinMarginPct > 0`** —
  price sort then renders visibly out of order, worst on the thin-margin rows a buyer studies
  hardest. Fixing it means either moving the floor into SQL or paging in memory, and that
  decision also governs the `spCatalog_Search` rewrite (inline TVF, per-sort `ORDER BY`,
  `OPTION (RECOMPILE)`) that is deliberately not done yet. Decide once, change the procedure
  once.

`CatalogItemModel.Cost` is a buy price and is currently selected on every catalog row.
`ProductCardView` excludes it; the detail page binds the raw model, so it is one field
reference away from publishing margin on a public page. Do not add it to a view record, and
prefer removing it from the projection over relying on review.

## Distributor feeds and image enrichment

A feed sync imports thousands of rows, so anything per-product and outbound happens out of
band. `DistributorFeedSyncService` does the import; `ProductImageBackgroundService` fills in
images afterwards, a batch at a time.

- **A finished sync signals the worker through `IImageEnrichmentSignal`** rather than leaving
  new products to wait out the idle sweep. The signal is a flag plus one replaceable
  `TaskCompletionSource`, **not** a `SemaphoreSlim` — the worker waits on it with `WhenAny`
  against a timer, and a semaphore strands the losing `WaitAsync` on its queue, where the next
  `Release()` feeds an orphan instead of the live waiter. That bug made Sync work exactly once
  per process. The `WhenAny` loser is cancelled through a linked token source, not abandoned.
- **An `HttpClient` timeout is an `OperationCanceledException` too.** Every catch around
  outbound work must filter on `stoppingToken.IsCancellationRequested`, or one slow response
  ends the worker silently for the life of the process.
- **`Considered` counts candidates fetched, not progressed.** Only a lookup that reaches a
  verdict stamps `ImageLookupUtc` and drops the row from the candidate set, so a batch where
  every call threw leaves the same rows waiting. Progress is `Failed < Considered`; a batch
  that clears nothing backs off by doubling rather than retrying at the batch pause.
- **`dbo.BrandAlias` translates feed brand vocabulary for the content provider.** A feed files
  HP Inc. under `HPINC`, which matches nothing at Icecat; `UNIVERSAL` is not a manufacturer at
  all, and a NULL `IcecatBrand` suppresses the brand lookup so only the EAN fallback runs. An
  unmapped brand passes through unchanged, because most of them are already right.
- **`BrandAliasResolver` is a singleton and takes a loader delegate, not its data access.** A
  singleton's factory closes over the *root* provider, so resolving a transient `IDisposable`
  from it leaks one per refresh for the life of the host. The composition root scopes each
  refresh instead.

**Icecat's free tier covers 3.8% of this catalog** — a full pass over 1,676 live products
matched 63. The feed carries an Icecat id but no image URLs. Treat images as an unsolved
sourcing problem, not a code problem, and do not build storefront features that assume a
product has one.

## SMPortal architecture

`IAdminDataService` is the single seam the admin pages bind to. Its **reads are synchronous** against
an in-memory snapshot that `EnsureLoadedAsync` fills, which `AdminLayout` awaits before rendering.
Mutations are `Task`-returning (a WASM client cannot block on HTTP) and end with `RefreshAsync()`.

Consequence: after any mutation, re-read the entity from the service — the object captured on page load
is stale. Detail pages do this in a `Recalc()` that starts with `_q = Data.GetQuote(Id)`.

The UI models key on human references (`"QT-0041"`, `"SO-0012"`, SKUs), not database ints. Mutations
resolve those to ids through `_accountIds` / `_groupSlugs` lookups or by re-fetching the catalog.
When a UI model needs a database id — as `QuoteLine.LineId` does so a row can be deleted — it has to
be added to the model *and* to the `Map*` projection.

### Blazor traps that have already cost time here

- **`Value="_search"` on a `string` parameter passes the literal text `"_search"`.** The `@` is not
  optional: write `Value="@_search"`. This was live at 8 call sites and made `SearchBox`'s parent-sync
  logic permanently dead.
- **Dirty inputs stop following the `value` attribute.** Once a user types, Blazor writing the
  attribute no longer updates the DOM property, so setting text in code appears to do nothing. Force a
  new element with `@key` — see `_textStamp` in `Shared/Admin/Combo.razor`.
- **One UI thread.** Fuzzy search over the catalogue blocks paint. The pattern is: raise the busy flag,
  `StateHasChanged()`, `await Task.Delay(80)` (16ms is not enough for a frame), *then* apply the term
  and do the work. Results are memoised in `SearchCache`.
- **Rebuilding SMPortal under a running dev server breaks SRI**: `Failed to find a valid digest in the
  'integrity' attribute`. Stop and restart the preview after a build.
- **CSS order:** `index.html` loads `app.css` then `admin.css`, so admin wins equal-specificity ties.
  Scoped `.razor.css` gains a `[b-xxxxx]` attribute and outranks both — use it to beat a global rule.
  `::deep` is needed to reach markup a child component renders (e.g. `NavLink`'s own `<a>`).

## Conventions

Comments in this codebase explain **why**, not what — usually the constraint or the bug that forced
the shape of the code. Match that; a comment restating the line below it does not belong.

Search is `FuzzySearch.Rank(source, term, textFields, codeFields)`. It runs literal matching first and
only falls back to FuzzySharp for a single-token term. **Code fields (SKU, MPN, EAN) get no fuzzy
pass** — same-shaped sibling identifiers scored above threshold and returned unrelated rows.

From `.github/copilot-instructions.md`:

- Keep the Aspire setup Azure-ready (publishes as Azure Container Apps) but **do not deploy to Azure**.
- Keep local orchestration on a persistent SQL Server container for both databases.
- Keep `StockApi` on HTTPS port 7042 — the Blazor and WPF clients depend on it.
