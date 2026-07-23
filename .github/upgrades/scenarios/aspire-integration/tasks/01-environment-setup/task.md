# 01-environment-setup: Create Aspire environment skeleton

Re-verify the Aspire CLI and create the supported AppHost skeleton with the non-interactive Aspire initialization workflow. Preserve the existing repository structure and add generated Aspire projects to the solution when required.

**Done when**: The Aspire CLI is available, the AppHost skeleton and Aspire configuration exist, generated projects are represented in the solution, and the affected projects build.

## Research Findings

### Projects Affected
- The Aspire CLI will create a new AppHost skeleton at the repository root; existing application projects are not edited by this task.
- `StockManager.sln` may be updated by the CLI to include generated Aspire projects.

### Environment
- Aspire CLI 13.4.6 was installed and updated during the compatibility pre-check.
- No existing AppHost SDK project, Aspire shared project, or Aspire hosting package was detected, so `aspire init` is required.
- All application `.csproj` files target Aspire-compatible .NET 10 TFMs.

### Execution Decision
- This is an atomic environment-bootstrap task: one CLI initialization concern with a clear build validation gate.
- Use `aspire init --non-interactive --suppress-agent-init --nologo`; do not manually recreate or relocate generated files.
