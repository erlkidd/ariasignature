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
- API exposes baseline endpoints for disks, SMART, backups, logs, and status.
- Swagger/OpenAPI endpoint is enabled for local integration testing.

## Icon asset policy

- Source icon file is tracked in repository root as `icon.png`.
- The same icon will be propagated to:
  - Desktop UI executable metadata.
  - Tray icon.
  - Installer branding.
