# AriaSignature Developer Guide

## Branching and delivery

- Primary release branch: `production`.
- Development branch for implementation work: `test/agent-work`.
- Every implementation increment is committed and pushed to `test/agent-work`.
- Promotion to `production` is a manual merge by repository owner.

## Current architecture baseline

- Service-first model: `AriaSignature.Service` is the runtime host.
- Local API is self-hosted by service process on `127.0.0.1`.
- API versioning baseline path: `/api/v1`.
- Layered projects are split into domain/application/infrastructure/api/ui.

## Implemented skeleton behavior

- Service runs heartbeat background worker.
- Service starts and stops local API lifecycle as hosted service.
- SMART refresh is scheduled with Quartz job (`SmartRefreshJob`) using cron from config.
- API exposes baseline endpoints for disks, SMART, backups, logs, and status.
- Swagger/OpenAPI endpoint is enabled for local integration testing.

## SMART module baseline

- `IDiskTelemetryCollector` collects physical disk inventory.
- `WmiDiskTelemetryCollector` reads Win32 disk metadata via WMI on Windows.
- `DiskTelemetryService` orchestrates refresh and query operations.
- `InMemoryDiskTelemetryRepository` stores latest disk snapshots and SMART history.

## Backup module baseline

- `IBackupService` provides backup job CRUD and execution APIs.
- `BackupService` applies retry policy (3 attempts) and writes execution logs.
- `BackupExecutor` supports file copy backups and MSSQL `BACKUP DATABASE` flow.
- Retention policy is applied after each successful backup run.

## Data persistence

- SQLite is used as the primary local storage engine.
- Schema is initialized on service startup by `DatabaseInitializationHostedService`.
- Repositories for disks/SMART and backup jobs/logs are backed by SQLite tables.

## Icon asset policy

- Source icon file is tracked in repository root as `icon.png`.
- The same icon will be propagated to:
  - Desktop UI executable metadata.
  - Tray icon.
  - Installer branding.
