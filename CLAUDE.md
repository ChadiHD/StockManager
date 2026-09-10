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

.NET Aspire orchestrates everything. One API serves three clients:

| Project | Role |
| --- | --- |
| `StockManager.AppHost` | Aspire orchestrator. **The entry point** — dashboard on 17291 |
| `StockApi` | ASP.NET Core Web API, HTTPS **pinned to 7042** (clients hardcode it) |
| `SMDataManager.Library` | Dapper + stored-procedure data access. Shared by the API only |
| `SMDatabase` | Classic SSDT `.sqlproj` — tables and stored procedures |
| `SMPortal` | Blazor **WebAssembly** admin portal (`/admin/*`), the active front end |
| `SMDesktopUI` + `.Library` | WPF POS desktop app (legacy, still shipped) |
| `SMDesktopUI.UITests` | xunit + FlaUI UI automation for the WPF app |
| `StockManager.ServiceDefaults` | Shared Aspire telemetry, health checks and resilience |

`SMDataManager/` (a .NET Framework 4.8 Web API with `packages.config`) is **not in the solution**.
It is dead code whose controllers mirror `StockApi`'s. Never edit it — changes there do nothing.

## Build and run

Run everything through the app host:

```bash
dotnet run --project StockManager.AppHost/StockManager.AppHost.csproj
```

Aspire provisions SQL Server as a **persistent container** (`AddAzureSqlServer("sql").RunAsContainer()`
with volume `stockmanager-sql-data`) holding `ApiAuthDb` and `SMDatabase`. The connection strings in
`StockApi/appsettings.json` are overridden by Aspire at run time — the `SITIHAPIB` value there is not
what a running app uses.

### SMDatabase needs Visual Studio MSBuild, not the dotnet CLI

`dotnet build` on `SMDatabase.sqlproj` — or on `StockManager.sln`, which contains it — fails with
`error MSB4278` (missing SSDT targets). Use VS MSBuild:

```bash
"$("/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe')" SMDatabase/SMDatabase.sqlproj -v:m -nologo
```

The app host **refuses to start** if `SMDatabase/bin/Debug/SMDatabase.dacpac` is missing or older than
any `.sql` file under `SMDatabase/`. So after touching SQL: rebuild the sqlproj, then restart the app
host. `smdatabase-schema` publishes the DACPAC automatically (`WithSkipWhenDeployed`, so an unchanged
DACPAC is skipped).

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

Adding a stored procedure takes two steps: create the `.sql` file under
`SMDatabase/dbo/Store Procedures/`, **and** add a `<Build Include="..." />` entry to
`SMDatabase.sqlproj`. That project lists every file explicitly; a file left out of it silently never
deploys.

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
