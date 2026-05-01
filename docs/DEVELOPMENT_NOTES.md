# AriaSignature Development Notes

## Git workflow

- `production` branch is the release branch.
- `test/agent-work` is the integration branch for AI-driven implementation work.
- All development commits are made in `test/agent-work` and pushed to origin.
- Merge to `production` is handled manually by repository owner.

## Icon usage policy

- Application icon source is expected at project root: `icon.png`.
- The same icon must be applied to:
  - WPF desktop executable.
  - System tray icon.
  - Installer package icon.

## Current status

- Clean Architecture solution scaffold created.
- Domain entities and enums created as baseline for disk and backup modules.
- Service-hosted local API is running with `/api/v1` endpoints.
- SMART baseline implemented (WMI collector + in-memory history + Quartz refresh job).
- Backup baseline implemented (job CRUD + run + retry + retention + logs).
- SQLite persistence and schema initialization added for disks, metrics, jobs, and logs.
- WPF MVVM shell added with required operational tabs and linked icon resource.
- Tray icon baseline and installer script baseline added.
- API integration tests added for system status and disk endpoints.
- Russian localization baseline enforced for installer, uninstaller, and UI texts.
- Automatic cron-driven backup scheduler and UI API synchronization added.
- Installer elevation, service recovery, tray autostart, and close-to-tray behavior added.
- `.gitignore` expanded for .NET/WPF/SQLite runtime and test artifacts.
- Disk diagnostics upgraded: service now reads SMART ATA attributes, power counters, and per-disk volume capacity via WMI.
- UI now works in strict service-first mode for disk data (no direct local disk provider in UI process).
- Backup creation form now supports both `.1CD` file mode and `MSSQL` mode.
- Service warmup now performs initial telemetry refresh on startup.
- Backup lifecycle in UI expanded: inline edit, enable/disable, run-now, delete.
- Added interactive source/destination pickers for backup task creation.
- Added UI settings for runtime theme switching and autostart management.
- Added dashboard counters and custom cron input in backup creation flow.
- API not-found cases moved to explicit `application/problem+json` semantics.
- Added strict backup request preflight checks (file existence, destination access, live MSSQL connectivity).
- Backup executor now prevents empty MSSQL artifacts and produces `.rar` archives.
- Added release gate assets: `scripts/release-gate.ps1` and `docs/RELEASE_GATE.md`.
- Installer branding now uses `SetupIconFile` with shared `icon.ico`.
- Build verification is green for all projects.
