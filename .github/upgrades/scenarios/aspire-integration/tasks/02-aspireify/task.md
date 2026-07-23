# 02-aspireify: Wire the StockManager resource graph

Use the repository-local aspireify guidance to model StockApi, SMPortal, and SMDesktopUI as runnable resources. Wire frontend and desktop API dependencies, determine the appropriate SQL Server resources, add ServiceDefaults where supported, and preserve non-Aspire launch behavior.

**Done when**: The AppHost resource graph builds, project dependencies and SQL resources are wired using verified Aspire APIs, and all declared local resources start or have documented actionable blockers.

## Research Findings

### Projects Affected
- `StockManager.AppHost` — declare all Aspire resources and project relationships.
- `StockApi` — reference ServiceDefaults, expose health endpoints, and consume Aspire-injected `DefaultConnection` and `SMDatabase` connection strings.
- `SMPortal` — run as the Blazor WebAssembly frontend and depend on `StockApi`; retain its browser-readable API configuration.
- `SMDesktopUI` — run as the WPF client and depend on `StockApi`; retain its existing API configuration.
- A generated `StockManager.ServiceDefaults` project — shared observability, health checks, service discovery, and HTTP resilience for the ASP.NET Core API.

### Current Communication and Data Dependencies
- `SMPortal` and `SMDesktopUI` both call `StockApi` at the fixed development HTTPS endpoint `https://localhost:7042`.
- `StockApi` uses SQL Server connection string names `DefaultConnection` (`ApiAuthDb`) and `SMDatabase` (`SMDatabase`).
- No Docker Compose files, non-.NET services, SDK pins, caches, or message brokers were found.

### Verified Aspire APIs and Packages
- Current first-party SQL Server integration: `Aspire.Hosting.SqlServer` 13.4.6 (`aspire integration search sqlserver`).
- Verified APIs: `AddSqlServer`, `AddDatabase(resourceName, databaseName)`, `WithDataVolume`, `WithReference`, `WaitFor`, `WithHttpsEndpoint`, and `WithHttpHealthCheck`.
- Full-project AppHost mode uses explicit project references so generated `Projects.*` types are available.

### Proposed Resource Graph
- `sql` (persistent SQL Server container)
  - `DefaultConnection` → database `ApiAuthDb`
  - `SMDatabase` → database `SMDatabase`
- `stock-api` → references and waits for both databases; exposes the existing HTTPS port and `/health`.
- `sm-portal` → references and waits for `stock-api`; external browser endpoint.
- `sm-desktop` → references and waits for `stock-api`; desktop-only local resource.

### Decisions and Risks
- Preserve API port 7042 because both the browser application and desktop client are hardcoded to it; changing to dynamic ports would require broader client configuration work.
- Add ServiceDefaults only to `StockApi`; Blazor WebAssembly and WPF do not use the ASP.NET Core `WebApplicationBuilder`/`WebApplication` bootstrap shape.
- The SQL container starts empty. Identity migrations and the `SMDatabase.sqlproj` schema may require a separate deployment/seed step before all application operations work.
- This remains one atomic graph-wiring concern; local startup is the validation gate for the complete resource graph.
- User approved the full local graph: persistent SQL Server with both databases, StockApi ServiceDefaults, and fixed HTTPS port 7042.
