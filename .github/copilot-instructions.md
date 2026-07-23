# Copilot Instructions

## Project Guidelines
- For the StockManager Aspire integration, use inner-loop plus Azure-ready configuration, but do not perform the Azure deployment.
- For StockManager local Aspire orchestration, use a persistent SQL Server container for ApiAuthDb and SMDatabase, add ServiceDefaults to StockApi, and preserve StockApi HTTPS port 7042 for the existing Blazor and WPF clients.