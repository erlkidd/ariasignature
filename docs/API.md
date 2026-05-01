# AriaSignature API (v1)

Версия документа: 0.2.3.

## 1. Общие параметры

- Base URL: `http://127.0.0.1:{port}/api/v1`
- Порт по умолчанию: `5160`
- Формат: `application/json`
- OpenAPI/Swagger: `/swagger`

Источник фактических данных — `AriaSignatureService` (телеметрия, backup, журнал).  
API публикует результат работы службы и ограниченный набор локальных операторских операций.

Поле версии сервиса доступно через `GET /status`.

## 2. Контракт ошибок

- `400 Bad Request` — валидация, `application/problem+json`
- `404 Not Found` — отсутствующий ресурс, `application/problem+json`
- `500 Internal Server Error` — непредвиденная ошибка, `application/problem+json` + `traceId`

## 3. Endpoints

### 3.1 Service status

- `GET /status`
  - Назначение: проверка доступности и версии сервиса.
  - Ответ: `status`, `timestampUtc`, `version`.

### 3.2 Settings

- `GET /settings`
  - Назначение: чтение текущих параметров (`apiPort`, `smartMonitoringCron`, `note`).

- `PUT /settings`
  - Назначение: обновление параметров.
  - Примечание: локальная операторская операция для панели администрирования.
  - Тело:
    - `apiPort` (optional, `1..65535`)
    - `smartMonitoringCron` (optional, Quartz expression)

### 3.3 Disk telemetry

- `GET /disks`
  - Назначение: список диагностируемых дисков.
  - Ответ содержит: идентификатор, модель, интерфейс, объемы, температуру, health, SMART-счетчики, статус.
  - Расширенные поля наблюдаемости: `smartCtlUsed`, `wmiUsed`, `storageReliabilityUsed`, `telemetryConfidence`, `telemetryDegradationReason`.
  - Nullable-поля телеметрии: `temperatureCelsius`, `healthPercent`, `ssdLifeRemainingPercent`.

- `POST /disks/refresh`
  - Назначение: принудительный пересчет среза телеметрии.
  - Источники: primary `smartctl` + fallback WMI/Storage Reliability.

- `GET /disks/{id}`
  - Назначение: карточка диска.
  - `404`, если диск не найден.

- `GET /disks/{id}/smart`
  - Назначение: история SMART-метрик (последние записи).
  - `404`, если диск не найден.

### 3.4 Backup jobs

- `GET /backups`
  - Назначение: список задач.

- `POST /backups`
  - Назначение: создание задачи.
  - Примечание: локальная операторская операция.

- `PUT /backups/{id}`
  - Назначение: обновление задачи.
  - Примечание: локальная операторская операция.

- `DELETE /backups/{id}`
  - Назначение: удаление задачи.
  - Примечание: локальная операторская операция.

- `POST /backups/{id}/run`
  - Назначение: немедленный запуск задачи.
  - Примечание: локальная операторская операция.

- `POST /backups/test-mssql`
  - Назначение: проверка подключения к MSSQL без сохранения задачи.

### 3.5 Backup logs

- `GET /backups/logs`
  - Назначение: журнал выполнения задач.
  - Query:
    - `status` (`Succeeded` | `Failed`)
    - `from` (ISO-8601)
    - `to` (ISO-8601)

## 4. Модель задачи архивации

### 4.1 `file`

- `source` — абсолютный путь к `.1CD`
- `destination` — абсолютный путь к каталогу архивов
- `scheduleCron` — Quartz cron
- `retentionCount` — количество хранимых архивов

### 4.2 `msSql`

Варианты задания подключения:
- `source` как готовая connection string
- или объект `msSql`:
  - `server`
  - `database`
  - `auth` (`sql` | `windows`)
  - при `sql`: `user`, `password`
  - `trustServerCertificate` (optional)

## 5. Валидация

- обязательные поля;
- корректность Quartz cron;
- `retentionCount > 0`;
- абсолютные пути и доступность destination;
- для `file`: существование source;
- для `msSql`: валидность подключения и доступ к базе.

## 6. Эксплуатационные заметки

- API локальный; публикация наружу выполняется сетевой конфигурацией заказчика.
- UI использует тот же API-контур, что и внешние интеграции.
- Сервис является системной точкой выполнения: сбор данных, архивирование, журналирование и выдача API.
