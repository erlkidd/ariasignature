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
- Build verification is green for all projects.
