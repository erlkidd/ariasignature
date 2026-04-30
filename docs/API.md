# AriaSignature API Documentation

## Base

- Base URL: `http://127.0.0.1:{port}/api/v1`
- Default port in current service config: `5160`
- Media type: `application/json`

## Endpoints

### System

- `GET /status` - service status and timestamp

### Disks

- `GET /disks` - list disks
- `GET /disks/{id}` - get disk by ID

### SMART

- `GET /disks/{id}/smart` - get SMART metrics by disk ID

### Backups

- `GET /backups` - list backup jobs
- `POST /backups` - create backup job
- `PUT /backups/{id}` - update backup job
- `DELETE /backups/{id}` - delete backup job
- `POST /backups/{id}/run` - run backup job immediately

### Logs

- `GET /backups/logs` - list backup execution logs

## OpenAPI

- Swagger UI is available at `/swagger`.
- OpenAPI JSON is available via Swagger endpoint set.
