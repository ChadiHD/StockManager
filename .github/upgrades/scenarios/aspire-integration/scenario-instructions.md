# Aspire Integration

## Preferences
- **Flow Mode**: Automatic
- **Integration Scope**: Add Aspire orchestration to the StockManager solution, prioritizing the StockApi and Blazor WebAssembly frontend.
- **Integration Mode**: Inner-loop + Azure-ready; configure publishing support but do not deploy.
- **Local Resource Graph**: Use a persistent Aspire SQL Server container with `ApiAuthDb` and `SMDatabase`, add ServiceDefaults to StockApi, and keep StockApi HTTPS on port 7042 for existing Blazor and WPF clients.

## Source Control
- **Source Branch**: main
- **Working Branch**: aspire-integration
- **Commit Strategy**: After Each Task
- **Branch Sync**: Auto (Merge)

## Key Decisions Log
- **2026-03-27**: Confirmed automatic execution, a dedicated `aspire-integration` branch, committing pending work before branching, and committing after each task.
- **2026-03-27**: Selected the Azure-ready Aspire integration mode for the Web API and Blazor application; Azure deployment remains a manual follow-up.
- **2026-03-27**: Approved the full local Aspire graph with SQL Server resources and fixed API port 7042; database schema deployment may be deferred if it cannot be automated safely.
