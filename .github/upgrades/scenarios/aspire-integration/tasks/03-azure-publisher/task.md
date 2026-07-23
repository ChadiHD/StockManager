# 03-azure-publisher: Configure Azure publishing

Select the Azure publishing target, add its supported Aspire integration, and configure the AppHost for future deployment without invoking an interactive deployment command.

**Done when**: The publisher package and AppHost publishing configuration are present, no Azure deployment has been started, and the AppHost builds without Aspire-introduced warnings.

## Research Findings

### Selected Publisher
- Azure Container Apps (ACA), explicitly selected by the user.
- Configure deployment readiness only; never run `aspire deploy` or another interactive Azure provisioning command.

### Projects and Resources Affected
- `StockManager.AppHost` — add ACA and Azure SQL publishing integrations and publishing annotations.
- `stock-api` and `sm-portal` are deployable project resources and will receive `PublishAsAzureContainerApp(...)`.
- `sm-desktop` is a WPF desktop resource and remains inner-loop-only.
- The local `sql` resource will publish as Azure SQL Database while retaining its local SQL Server container behavior.

### Verified APIs and Packages
- `Aspire.Hosting.Azure.AppContainers` 13.4.6 provides `AddAzureContainerAppEnvironment` and `PublishAsAzureContainerApp(Action<AzureResourceInfrastructure, ContainerApp>)`.
- `Aspire.Hosting.Azure.Sql` 13.4.6 uses `AddAzureSqlServer(...).RunAsContainer(...)` to model Azure SQL for publishing while retaining a local SQL Server container. The older `PublishAsAzureSqlDatabase()` API is obsolete.
- Both packages are available through the current Aspire integration catalog.

### Execution Decision
- This is one atomic publishing concern in a single AppHost project.
- Add one ACA environment, publish the API and Blazor WebAssembly project to ACA, publish SQL as Azure SQL, and exclude WPF from cloud publishing.
