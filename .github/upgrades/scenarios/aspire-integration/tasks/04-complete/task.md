# 04-complete: Validate and document completion

Run the final build, relevant tests, and Aspire resource checks. Record the dashboard URL, resource status, skipped or deferred items, and manual Azure deployment instructions.

**Done when**: Validation results and resource status are recorded, all tasks are complete, and the user has actionable local-run and optional Azure-deployment instructions.

## Research Findings

### Final Validation Scope
- Build the full Visual Studio solution, including the SSDT database project and generated Aspire projects.
- Confirm no test projects are present.
- Start the AppHost, wait for SQL, both database resources, StockApi, the Blazor WebAssembly frontend, and the WPF client.
- Capture a fresh dashboard login URL and final `aspire describe` resource summary.

### Completion Surface
- Local resources: `sql`, `DefaultConnection`, `SMDatabase`, `stock-api`, `sm-portal`, and `sm-desktop`.
- Azure publisher: Azure Container Apps for StockApi and SMPortal; Azure SQL for the database resources; WPF remains local-only.
- No projects were skipped for incompatible TFMs.
- Deferred item: deploy the `SMDatabase.sqlproj` schema and apply Identity migrations/seed data to new database environments.
- Azure deployment remains manual through `aspire deploy` or another preferred deployment tool.

### Execution Decision
- This task is atomic validation and documentation work with no additional application code changes expected.
