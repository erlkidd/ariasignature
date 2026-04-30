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

## Planned next capabilities

- SMART collection and health evaluation.
- File-based and MSSQL backup execution module.
- Retention policy and backup logs in persistent storage.
