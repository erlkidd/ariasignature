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

## SMART module

- `IDiskTelemetryCollector` collects physical disk inventory.
- `WmiDiskTelemetryCollector` reads Win32 disk metadata, SMART ATA attributes, and per-physical-disk volume mapping via WMI on Windows.
- `DiskTelemetryService` orchestrates refresh and query operations.
- `SqliteDiskTelemetryRepository` stores disk snapshots and SMART history.

## Backup module

- `IBackupService` provides backup job CRUD and execution APIs.
- `BackupService` applies retry policy (3 attempts) and writes execution logs.
- `BackupExecutor` supports file copy backups and MSSQL `BACKUP DATABASE` flow.
- Retention policy is applied after each successful backup run.
- `BackupSchedulerHostedService` polls due cron jobs and runs them automatically.
- API validation now includes type-specific checks for `File` vs `MsSql`.

## Data persistence

- SQLite is used as the primary local storage engine.
- Schema is initialized on service startup by `DatabaseInitializationHostedService`.
- Repositories for disks/SMART and backup jobs/logs are backed by SQLite tables.

## UI baseline (MVVM)

- WPF shell now uses MVVM data binding with `MainViewModel`.
- Main window includes required tabs: Disks, Backup, Settings.
- No business logic is implemented in `code-behind`.
- Root `icon.png` is linked as UI resource (`Assets/icon.png`).
- UI pulls live data from local API via `AriaApiClient` and manual refresh command.
- UI does not use direct local disk probing anymore; disk data source is only the service API.
- Backup creation UI supports task type selection (`File` / `MsSql`) with source semantics per type.
- Backup jobs in UI support inline update, enable/disable toggle, manual run, and delete.
- App resources include `Themes/AriaTheme.xaml` as a shared visual dictionary.

## Icon asset policy

- Source icon file is tracked in repository root as `icon.png`.
- The same icon will be propagated to:
  - Desktop UI executable and resources.
  - Tray icon.
  - Installer branding.

## Installer baseline

- Inno Setup script is available at `installer/inno/AriaSignature.iss`.
- Script installs UI and service binaries from publish folders.
- Script registers Windows service and starts it after installation.
- Installer and uninstaller UX language is fixed to Russian.

## Localization requirement

- Mandatory language for installer, uninstaller, and UI runtime text is Russian.
- Any newly added user-facing strings must be introduced in Russian by default.

## Production readiness checkpoints

- Installer runs with admin elevation and configures service recovery.
- Application supports startup in tray mode via `--tray`.
- Window close action minimizes to tray; hard exit is tray-menu driven.
- Service executes telemetry warmup on startup to avoid empty first API response.

## Testing baseline

- API integration tests run via `WebApplicationFactory`.
- API tests validate status/disks endpoints, backup CRUD/run, and problem+json not-found semantics.
- Release gate automation script is available at `scripts/release-gate.ps1`.
- RC checklist is documented in `docs/RELEASE_GATE.md`.
