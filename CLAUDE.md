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

.NET Aspire orchestrates everything.

| Project | Role |
| --- | --- |
| `StockManager.AppHost` | Aspire orchestrator. **The entry point** — dashboard on 17291 |
| `StockApi` | ASP.NET Core Web API, HTTPS **pinned to 7042** (clients hardcode it) |
| `SMDataManager.Library` | Dapper + stored-procedure data access. Shared by `StockApi` and `SMStore` |
| `SMDatabase` | Classic SSDT `.sqlproj` — tables and stored procedures |
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
  leak, not a display bug. Scoped today: `Account`, `CustomerGroup`, `Quote`, `Purchase`
  (portal orders only), `DistributorFeed`.
- **Nothing outside `SMStore/Ordering/` branches on `Site.OrderMode`.** Ask
  `OrderingModeProvider.Current` instead.
- **`wwwroot/app.css` holds no colour of its own.** It reads custom properties that a theme
  under `wwwroot/sites/{SiteKey}/` defines. `SiteThemeResolver` falls back to the `default`
  theme for any asset a site is missing.
- `Sites:ForceSiteKey` pins every request to one store for local work. `SMStore` refuses to
  start with it set outside Development.

Data Protection keys are shared by `StockApi` and `SMStore` through `AddSharedDataProtection`,
persisted to `dbo.DataProtectionKeys` in `ApiAuthDb`. Both apps must keep the same application
name, or neither can read the other's cookies or the feed credentials in `dbo.DistributorFeed`.

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
on launch. There are no tests for `StockApi`, `SMDataManager.Library` or `SMPortal`.

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
`SMDatabase/dbo/Store Procedures/`. The SDK-style project globs `**/*.sql`, so there is no longer
a `<Build Include="..." />` list to keep in step — that was the old classic-SSDT footgun, where a
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
