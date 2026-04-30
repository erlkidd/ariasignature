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
- `GET /disks/{id}` - get disk by ID (`404` as `application/problem+json` if missing)

### SMART

- `GET /disks/{id}/smart` - get stored SMART metric history by disk ID (`404` problem response if disk missing)

### Backups

- `GET /backups` - list backup jobs
- `POST /backups` - create backup job (validates cron, name, source, destination, retention)
- `PUT /backups/{id}` - update backup job (same validation rules)
- `DELETE /backups/{id}` - delete backup job (`404` problem response if missing)
- `POST /backups/{id}/run` - run backup job immediately (with retry policy)

`Type` in backup contract supports:
- `File` - `Source` is path to `.1CD` (or source folder/file), `Destination` is target directory.
- `MsSql` - `Source` is SQL connection string, `Destination` is target directory for generated backup archive.

Validation details:
- `Source` and `Destination` must be absolute paths for file-oriented operations.
- `MsSql` source must contain `Server` and `Database`/`Initial Catalog`.
- `MsSql` connection is validated online before job creation/update.
- `Source` and `Destination` cannot resolve to the same path for `File` mode.

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
- Not-found domain cases are also returned as `application/problem+json`.
- Backup artifacts are produced as `.rar` archives; missing archiver is returned as execution error.
