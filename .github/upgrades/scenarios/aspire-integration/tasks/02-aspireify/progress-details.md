## Files Modified
- `StockManager.AppHost/AppHost.cs`
- `StockManager.AppHost/StockManager.AppHost.csproj`
- `StockManager.ServiceDefaults/StockManager.ServiceDefaults.csproj`
- `StockManager.ServiceDefaults/Extensions.cs`
- `StockApi/Program.cs`
- `StockApi/StockApi.csproj`
- `SMDataManager.Library/SMDataManager.Library.csproj`
- `StockManager.sln`
- Aspire workflow artifacts under `.github/upgrades/scenarios/aspire-integration/`

## Build Result
- Errors: 0
- Aspire-introduced warnings: 0
- Full solution build: successful in Visual Studio
- AppHost `dotnet build`: successful; existing application warnings remain outside the Aspire-integration warning scope.

## Test Result
- Tests run: 0
- No test projects were discovered in the solution.

## Runtime Validation
- Dashboard: `https://localhost:17291/login?t=5f0f5806d8b51020bce2e9fe6167ea15` (validation run; AppHost was stopped afterward)
- `sql`: healthy/running
- `DefaultConnection`: healthy/running
- `SMDatabase`: healthy/running
- `stock-api`: healthy/running at `https://localhost:7042`
- `sm-portal`: healthy/running at `https://localhost:7199`
- `sm-desktop`: healthy/running
- All declared resources passed `aspire wait`; `aspire describe --format Json` showed the expected references and endpoints; `aspire stop` completed successfully.

## Changes Summary
- Added a persistent Aspire SQL Server resource with `ApiAuthDb` and `SMDatabase` database resources.
- Wired StockApi to both database connection strings and startup dependencies.
- Added ServiceDefaults observability, health checks, service discovery, and resilience to StockApi.
- Wired the Blazor WebAssembly and WPF clients to reference and wait for StockApi.
- Preserved StockApi HTTPS port 7042 for the existing client configuration.
- Added the ServiceDefaults project and all required project/package references to the solution.

## Issues Encountered
- `aspire start` initially failed with MSB4278 because `SMDataManager.Library` referenced the SSDT `.sqlproj`, which Aspire's dotnet-based build cannot compile. The invalid assembly dependency was removed while retaining `SMDatabase.sqlproj` in the solution.
- The SQL resources start as empty databases. Identity migrations and deployment of the `SMDatabase.sqlproj` schema remain required before database-backed application flows contain their expected schema/data.
- Aspire CLI 13.4.6 no longer supports the skill-documented `aspire ps --include-hidden`; resource validation used `aspire describe --format Json` and `aspire wait`.
