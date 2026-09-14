# StockManager.E2ETests

End-to-end tests that start the real Aspire application graph (`StockManager.AppHost`) and
drive real user journeys across `SMPortal` and `SMStore` with a real browser
(Microsoft.Playwright). They are slow, need a container runtime, and touch a real SQL Server
database, so they are not part of a routine inner-loop `dotnet test` — see "Running these
deliberately" below.

## What each journey covers

| Test class | Journey |
| --- | --- |
| `StockVisibilityJourneyTests` | An admin changes stock on a product in SMPortal; the customer-facing SMStore product page reflects it. |
| `AnonymousCatalogJourneyTests` | The anonymous catalog pages correctly, and `?page=abc` / `?page=99999999999` do not 500 (both are bugs CLAUDE.md records as having already happened). |
| `RegistrationApprovalJourneyTests` | A stranger applies on `/register` (with a real file upload), an admin approves the application with a customer group, and the applicant signs in and sees that group's prices. |
| `CrossTenantRefusalJourneyTests` | A customer session at one store does not work at another — the platform's central multi-tenant security property. |

**None of these has ever been observed to pass.** They are written to go the full distance —
no stub, no commented-out step, no `Skip` covering a missing feature — and every selector was
cross-checked against the `.razor` source it targets. But the suite has never completed a run,
so every claim above is a claim about the code, not about observed behaviour. Treat a first
green run as the thing that makes this table true, and expect selector drift until then.

One assertion is deliberately loose and should be tightened once a run has happened:
`AnonymousCatalogJourneyTests` asserts the malformed `?page=` cases return **less than 500**
rather than exactly 200, because "does not 500" is the contract CLAUDE.md records and nobody
has yet seen what the real status is.

## Prerequisites

1. **A container runtime.** `StockManager.AppHost` provisions SQL Server as a container
   (`AddAzureSqlServer("sql").RunAsContainer()`). **Podman Desktop** is what this was built
   against — nothing here assumes Docker specifically, and `ContainerRuntimeProbe` tries
   `docker` and `podman` in turn before asking Aspire to start anything. Either running is
   enough.
2. **Playwright's Chromium browser**, installed once per machine (it is not restored by
   `dotnet restore`/`dotnet build`):

   ```powershell
   dotnet build StockManager.E2ETests/StockManager.E2ETests.csproj
   pwsh StockManager.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium
   ```

   If `pwsh` is not installed, `powershell` works the same way on Windows.

Both are optional in the sense that the suite does not fail without them — see "Skipping
cleanly" below — but obviously no journey actually runs until both are present.

## Running these deliberately

This project is not (yet) referenced by `StockManager.sln` — that is added separately, not by
these tests. Run it directly by path:

```bash
dotnet test StockManager.E2ETests/StockManager.E2ETests.csproj
```

First run on a fresh SQL volume pulls the SQL Server image and publishes the whole SMDatabase
DACPAC, which is genuinely slow (CLAUDE.md says as much about the app host generally). The
fixture waits up to **10 minutes** for `stock-api` and `sm-store` to report healthy before
giving up; override it with `E2E_RESOURCE_TIMEOUT_MINUTES` if a given machine needs longer:

```bash
E2E_RESOURCE_TIMEOUT_MINUTES=20 dotnet test StockManager.E2ETests/StockManager.E2ETests.csproj
```

Every test in the assembly shares one app host and one browser (`AspireAppFixture`, an
`ICollectionFixture` on the assembly's one `[Collection]`), started once and torn down once —
starting the app host per test would pay the slow first-run cost on every single test.
Parallelization within that collection is switched off on purpose: every journey shares one SQL
Server container and one running set of apps, and two journeys racing (one inserting a `Site`
row that changes what SMPortal's store switcher defaults to while another is mid-journey, say)
would be a false failure with nothing wrong in the product underneath it.

### Keeping this out of a plain `dotnet test` of the solution

Once `StockManager.E2ETests` is added to `StockManager.sln`, a bare `dotnet test
StockManager.sln` will discover it like any other test project — nothing inside this project's
own directory can prevent that (a solution-level exclusion, e.g. a `.slnf` filter, or a CI
config that filters by project, lives outside `StockManager.E2ETests/` and is a decision for
whoever wires up the solution and CI). What this project *does* guarantee on its own is the
fallback the brief allows: every test skips cleanly, fast, and with a specific reason when a
container runtime or a Playwright browser is not available (see below) — a plain `dotnet test`
run on a machine with neither will see 4 skipped tests in a few seconds, not a hang or a wall
of container errors. Every test also carries `[SkippableFact]` rather than `[Fact]`, so a
solution-wide run can filter them out explicitly if that is preferred:

```bash
dotnet test StockManager.sln --filter "FullyQualifiedName!~StockManager.E2ETests"
```

## Skipping cleanly

Every test starts with:

```csharp
Skip.If(_fixture.StartupFailure is not null, _fixture.StartupFailure);
Skip.If(_fixture.BrowserUnavailable is not null, _fixture.BrowserUnavailable);
```

`AspireAppFixture.InitializeAsync` never throws. If neither `docker version` nor `podman
version` answers, or the app host fails to start, or its resources never become healthy within
the timeout, `StartupFailure` is set to a specific, actionable message and every test in the run
skips with it instead of the whole collection failing with a fixture-initialization error. The
same goes for `BrowserUnavailable` if Chromium fails to launch (almost always because it is not
installed yet — the skip message includes the exact `playwright.ps1 install chromium` command).

**CI must set `E2E_REQUIRE_APPHOST=1`.** Without it, an app host that will not start is a
skip, which is right on a workstation — the usual cause is that the developer already has the
app host running and StockApi is pinned to 7042, so a second one cannot bind. In a pipeline
that same skip would hide a real regression behind four green skips. With the variable set,
the startup exception is rethrown and the run goes red. A missing container runtime stays a
skip either way: that is a prerequisite, not a regression.

## Design notes worth knowing before changing this project

- **StockApi is reached at the hardcoded `https://localhost:7042`.** Every other endpoint
  (`sm-store`, `sm-portal`) is resolved dynamically via `app.GetEndpoint(resourceName,
  "https")`. This matches CLAUDE.md exactly: the port is pinned because the real Blazor and WPF
  clients hardcode it too, so resolving it "properly" here would test a guarantee nothing else
  in the codebase relies on.
- **No admin exists anywhere in a fresh database, and the product has no path to create the
  first one.** `POST /api/User/Admin/AddRole` is `[Authorize(Roles = "Admin")]`, and the
  anonymous `POST /api/User/Register` grants no role. `Infrastructure/IdentityTestSupport.cs`
  bootstraps one directly against `ApiAuthDb`, through the same `ApplicationDbContext` and
  `UserManager`/`RoleManager` the real hosts use — this is the one deliberate "go around the
  front door" in the admin-facing journeys, and it is there because there is currently no other
  way, not as a shortcut around something the UI can do.
- **A store's own category taxonomy has no admin screen yet.** A product is only visible on a
  storefront once `dbo.CategoryMapping` connects its `Category` string to a `dbo.SiteCategory`
  for that site (see `CategoryMapping.sql`'s own comment: "a feed value nobody has mapped stays
  invisible"). `Infrastructure/SqlTestData.cs` writes these two rows directly for the same
  reason as the admin bootstrap: there is nowhere in the product yet that would do it instead.
- **`CrossTenantRefusalJourneyTests` provisions its customer directly**, rather than through
  `/register` and an approval — it is testing what an *existing* session may do across two
  stores, not how one comes to exist; `RegistrationApprovalJourneyTests` is what exercises the
  real form and the real approval screen end to end.
- **Structural field lookups, not `GetByLabel`.** Several of SMPortal's own forms (for example
  `ProductDetail.razor`'s "Available units", `Groups.razor`'s "Base discount (%)") render a
  bare `<label>` as a sibling of its control rather than a wrapper, with no `for`/`id` link, so
  Playwright's label-based locators cannot find them. `AdminPortal.FieldControl(label)` finds
  the input/select/textarea inside whichever `.field` contains that label text instead — this
  is a workaround for the markup as it exists today, not a preferred pattern to copy elsewhere.
- **`IgnoreHTTPSErrors` is on for every browser context.** The ASP.NET Core development
  certificate covers `localhost` only. `CrossTenantRefusalJourneyTests` deliberately reaches
  the same `sm-store` endpoint as `127.0.0.1` to get a second `Host` header out of a real
  browser without editing a hosts file (`SiteResolver` keys strictly on `Request.Host.Host`),
  which fails certificate hostname validation by design.
