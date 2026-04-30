# AriaSignature API Documentation

## Base

- Base URL: `http://127.0.0.1:{port}/api/v1`
- Default port in current service config: `5160`
- Media type: `application/json`

## Endpoints

### System

- `GET /status` - service status and timestamp

### Disks

- `GET /disks` - list collected disks
- `GET /disks/{id}` - get disk by ID (404 if missing)

### SMART

- `GET /disks/{id}/smart` - get stored SMART metric history by disk ID

### Backups

- `GET /backups` - list backup jobs
- `POST /backups` - create backup job (validates cron, name, source, destination, retention)
- `PUT /backups/{id}` - update backup job (same validation rules)
- `DELETE /backups/{id}` - delete backup job (404 if missing)
- `POST /backups/{id}/run` - run backup job immediately (with retry policy)

### Logs

- `GET /backups/logs` - list backup execution logs

## OpenAPI

- Swagger UI is available at `/swagger`.
- OpenAPI JSON is available via Swagger endpoint set.

## Notes

- Current SMART refresh schedule is managed in service via Quartz cron (`SmartMonitoring:Cron`).
- API is local-only and designed for service-hosted runtime on Windows.
- API data is persisted to local SQLite database (`ConnectionStrings:AriaSignature`).
- Unhandled API exceptions are returned as `application/problem+json` with trace identifier.
