# Aspire Integration Assessment

## Compatibility

All runnable and supporting .NET projects use Aspire-compatible .NET 10 target frameworks.

| Project | Type | Target framework | Aspire role |
|---|---|---|---|
| `StockApi/StockApi.csproj` | ASP.NET Core Web API / Razor Pages | `net10.0` | Runnable backend service |
| `SMPortal/SMPortal.csproj` | Blazor WebAssembly | `net10.0` | Runnable frontend |
| `SMDesktopUI/SMDesktopUI.csproj` | WPF desktop application | `net10.0-windows7.0` | Runnable desktop client |
| `SMDataManager.Library/SMDataManager.Library.csproj` | Class library | `net10.0` | Backend data-access dependency |
| `SMDesktopUI.Library/SMDesktopUI.Library.csproj` | Class library | `net10.0` | Shared API client/models dependency |

No incompatible `.csproj` projects were found. `SMDatabase/SMDatabase.sqlproj` is a SQL database project rather than an Aspire-orchestrated .NET application.

## Inter-Service Communication Graph

### Direct calls

- `SMPortal` --HTTP--> `StockApi`: the Blazor WebAssembly application reads the API base URL from `SMPortal/wwwroot/appsettings.json` (`api = https://localhost:7042`) and uses the shared `APIHelper` client.
- `SMDesktopUI` --HTTP--> `StockApi`: the WPF application reads the API base URL from `SMDesktopUI/appsettings*.json` (`api = https://localhost:7042/`) and uses `SMDesktopUI.Library/Api/APIHelper.cs`.

### Data access

- `StockApi` uses `ConnectionStrings:DefaultConnection` for its EF Core Identity database.
- `StockApi`, through `SMDataManager.Library`, uses named SQL connection strings including `SMDatabase` for application data.

## Recommended Aspire Graph

- Orchestrate `StockApi` as the backend service.
- Orchestrate `SMPortal` as the Blazor WebAssembly frontend and reference/wait for `StockApi`.
- Include `SMDesktopUI` as a desktop client where supported by the generated AppHost and reference/wait for `StockApi`.
- Keep class libraries as project dependencies rather than standalone Aspire resources.
- Let the `aspireify` workflow determine the exact SQL Server integration and whether minimal configuration changes are needed for dynamic service discovery.

Infrastructure integrations will be verified by the `aspireify` workflow using the current Aspire CLI documentation before AppHost code is generated.
