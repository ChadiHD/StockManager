## Files Modified
- `StockManager.AppHost/AppHost.cs`
- `StockManager.AppHost/StockManager.AppHost.csproj`
- `.github/upgrades/scenarios/aspire-integration/scenario-instructions.md`
- `.github/upgrades/scenarios/aspire-integration/tasks/03-azure-publisher/task.md`
- `.github/copilot-instructions.md`

## Build Result
- Errors: 0
- Aspire-introduced warnings: 0
- AppHost build: successful
- Full solution build: successful in Visual Studio
- Existing warnings in application projects remain outside the Aspire-integration warning scope.

## Test Result
- Tests run: 0
- No test projects exist in the solution.

## Runtime Validation
- Dashboard during smoke test: `https://localhost:17291/login?t=27e93a4ae23874c7cf81181bf0d06645`
- `sql`, `DefaultConnection`, `SMDatabase`, `stock-api`, `sm-portal`, and `sm-desktop` all remained running/healthy.
- StockApi remained available at `https://localhost:7042`.
- The AppHost stopped cleanly after validation.

## Changes Summary
- Added `Aspire.Hosting.Azure.AppContainers` 13.4.6.
- Added `Aspire.Hosting.Azure.Sql` 13.4.6.
- Added an Azure Container Apps environment named `aca-env`.
- Configured `stock-api` and `sm-portal` with `PublishAsAzureContainerApp(...)`.
- Kept the WPF `sm-desktop` project local-only.
- Replaced the local-only SQL resource with `AddAzureSqlServer(...).RunAsContainer(...)`, preserving a persistent local SQL container while publishing as Azure SQL.
- No Azure deployment or interactive provisioning command was run.

## Issues Encountered
- The first `aspire add azure-appcontainers` attempt found a lingering AppHost process; the CLI stopped it and the retry succeeded.
- `PublishAsAzureSqlDatabase()` produced CS0618 in Aspire 13.4.6. It was replaced with the current `AddAzureSqlServer(...).RunAsContainer(...)` pattern, eliminating the introduced warning.
