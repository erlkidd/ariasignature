# AriaSignature — Product Technical Specification

Version: 0.2.2  
Status: Active baseline

## 1. Product objective

AriaSignature is a Windows desktop product with a service-first runtime model.  
The system must:
- continuously collect disk telemetry;
- execute backup jobs by schedule;
- expose produced operational data through a stable local REST API;
- provide a user-friendly local UI for setup and monitoring.

The target operating mode is autonomous execution at customer site after initial setup.

## 2. Scope

### Included
- Disk diagnostics (HDD/SSD/NVMe, best-effort USB).
- Backup orchestration for 1C file databases and MSSQL.
- Job scheduling, execution logging, retention.
- Local API publication for external consumers.
- Installer, Windows service registration, tray workflow, autostart.

### Excluded
- External transport/integration logic outside local API boundary.
- Cloud backend responsibility.
- Vendor-specific dashboards outside product UI.

## 3. Architecture constraints

- `AriaSignature.Service` is the execution core.
- `AriaSignature.UI` is an operator console, not the execution engine.
- API contract is versioned and served by service (`/api/v1`).
- Local persistence is SQLite.
- UI uses the same API contracts as external integrations.

## 4. Runtime requirements

- OS: Windows 10/11 x64.
- Service must auto-start after OS reboot.
- Tray mode must support background operation without active main window.
- Single-instance policy for UI process is mandatory.
- Product must remain operational if UI is closed.

## 5. Functional requirements

### 5.1 Disk diagnostics
- Collect model, serial, interface, media type, capacity, free/used space.
- Collect temperature, health estimate, SSD life, power-on hours, power cycles.
- Collect SMART counters (reallocated, pending, uncorrectable, related metrics).
- Maintain status classification (`ok` / `warning` / `critical`).
- Support manual and scheduled refresh.
- Persist history for SMART/telemetry trends.

Data sources (priority merge):
- WMI (`Win32_DiskDrive`, `MSStorageDriver_*`);
- Storage Reliability counters (`MSFT_StorageReliabilityCounter`);
- low-level SMART/NVMe via `smartctl` when available.

### 5.2 Backup management
- Support backup job types:
  - `file` (1C `.1CD`);
  - `msSql` (SQL Server backup flow).
- Support CRUD operations for jobs.
- Support manual run and schedule-driven run.
- Enforce retention count with automatic cleanup.
- Record execution logs (status, timestamps, size, message).

### 5.3 Settings and operations
- Configure API port and diagnostic schedule.
- Configure UI autostart.
- Provide service status and control actions from UI.
- Keep all operational actions available through UI without script usage.

## 6. API requirements

- REST + JSON, local bind by default.
- Version prefix: `/api/v1`.
- OpenAPI/Swagger must be available.
- Stable contracts, backward-safe evolution.
- Error contract: `application/problem+json`.

Minimum endpoint groups:
- service status;
- settings;
- disks + smart history;
- backup jobs;
- backup logs.

## 7. Non-functional requirements

- No stubs as final implementation.
- Deterministic install/upgrade/uninstall behavior.
- Input validation for all write operations.
- Structured logging for service execution paths.
- Build reproducibility via release gate script.

## 8. Build, release and versioning

Mandatory version synchronization:
- `Directory.Build.props`;
- `installer/inno/AriaSignature.iss`;
- `src/web/package.json` and lockfile;
- user/API docs versions.

Release gate entry point:
- `scripts/release-gate.ps1`

Expected artifact:
- `artifacts/installer/AriaSignature-Setup.exe`

## 9. Branching and delivery policy

- Development branch: `test/agent-work`.
- Release branch: `production`.
- Merge to `production` is owner-controlled.

## 10. Documentation governance

Documentation is part of the deliverable and must be updated in the same change set as behavior/contract changes.

Required docs set:
- `docs/USER_GUIDE.md`
- `docs/API.md`
- `docs/DEVELOPER_GUIDE.md`
- `docs/INSTALLER.md`
- `docs/RELEASE_GATE.md`
- `docs/DEVELOPMENT_NOTES.md`

## 11. Definition of done

A change is complete only if:
- functional behavior is implemented;
- tests/build pass;
- installer build succeeds;
- API contract remains consistent;
- documentation reflects actual state.