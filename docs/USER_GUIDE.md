# AriaSignature User Guide (Draft)

## What this system does

AriaSignature is a local Windows service and desktop application for:

- Monitoring HDD/SSD health metrics.
- Scheduling and running 1C backup tasks.
- Providing local API integration for 1C workflows.

## Runtime model

- The core service is always the source of truth.
- Desktop UI is a control panel and does not execute business logic directly.
- API is local-only and available on localhost.

## Current available functionality

- Service process skeleton is operational.
- Local API base path is `/api/v1`.
- Core status endpoint is available at `/api/v1/status`.
- Disk inventory endpoint is available at `/api/v1/disks`.
- SMART history endpoint is available at `/api/v1/disks/{id}/smart`.
- Backup tasks can be created/updated/deleted from API.
- Manual backup run endpoint is available at `/api/v1/backups/{id}/run`.
- Backup execution logs are available at `/api/v1/backups/logs`.
- Configuration and operational data are persisted in local SQLite storage.
- Desktop UI contains tabs for disks, backup, and settings.
- Application runs with tray icon support for quick restore/exit actions.
- UI can refresh operational data from local API with status of last sync.

## Planned next capabilities

- Expanded SMART attributes (temperature, hours, cycles, vendor-specific details).
- File-based and MSSQL backup execution module.
- Retention policy and backup logs in persistent storage.
