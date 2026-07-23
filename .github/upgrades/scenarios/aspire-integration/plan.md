# Aspire Integration Plan

## Overview

**Target**: Add Aspire orchestration to the StockManager .NET 10 solution.
**Scope**: Five compatible .NET projects, with StockApi, SMPortal, and SMDesktopUI as runnable resources and Azure-ready publishing configuration.

## Tasks

### 01-environment-setup: Create Aspire environment skeleton

Re-verify the Aspire CLI and create the supported AppHost skeleton with the non-interactive Aspire initialization workflow. Preserve the existing repository structure and add generated Aspire projects to the solution when required.

**Done when**: The Aspire CLI is available, the AppHost skeleton and Aspire configuration exist, generated projects are represented in the solution, and the affected projects build.

---

### 02-aspireify: Wire the StockManager resource graph

Use the repository-local aspireify guidance to model StockApi, SMPortal, and SMDesktopUI as runnable resources. Wire frontend and desktop API dependencies, determine the appropriate SQL Server resources, add ServiceDefaults where supported, and preserve non-Aspire launch behavior.

**Done when**: The AppHost resource graph builds, project dependencies and SQL resources are wired using verified Aspire APIs, and all declared local resources start or have documented actionable blockers.

---

### 03-azure-publisher: Configure Azure publishing

Select the Azure publishing target, add its supported Aspire integration, and configure the AppHost for future deployment without invoking an interactive deployment command.

**Done when**: The publisher package and AppHost publishing configuration are present, no Azure deployment has been started, and the AppHost builds without Aspire-introduced warnings.

---

### 04-complete: Validate and document completion

Run the final build, relevant tests, and Aspire resource checks. Record the dashboard URL, resource status, skipped or deferred items, and manual Azure deployment instructions.

**Done when**: Validation results and resource status are recorded, all tasks are complete, and the user has actionable local-run and optional Azure-deployment instructions.
