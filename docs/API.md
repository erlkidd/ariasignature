# Документация API AriaSignature

**Версия продукта (релиз):** 0.2.2 — совпадает с `Directory.Build.props`, установщиком (`MyAppVersion`) и `package.json` веб-панели.

Версия контракта: **v1**. Текущая сборка API также отражается в `GET /api/v1/status` (поле `version`).

## Базовые параметры

- Базовый URL: `http://127.0.0.1:{port}/api/v1`
- Порт по умолчанию: `5160` (можно изменить в таблице `AppSettings` ключа `Api:Port`; для вступления в силу обычно требуется перезапуск службы)
- Формат данных: `application/json`
- Перечисления в JSON: строки в **camelCase** (например тип задачи: `file`, `msSql`)

## Endpoints

### Состояние сервиса

- `GET /status` — состояние, метка времени и версия сборки API.

### Настройки приложения

- `GET /settings` — текущие значения: `apiPort`, `smartMonitoringCron`, пояснение о перезапуске службы.
- `PUT /settings` — тело JSON: `apiPort` (опционально), `smartMonitoringCron` (опционально, валидный Quartz cron).

### Диски

- `GET /disks` — список накопителей (модель, серийный номер, интерфейс, тип носителя, объёмы, температура, здоровье, ресурс SSD при наличии, SMART-счётчики, статус `ok` / `warning` / `critical`).
- `POST /disks/refresh` — принудительно пересобрать снимок WMI/томов и вернуть актуальный список (тот же формат, что у `GET /disks`). Панель управления вызывает этот метод при открытии и по кнопке обновления.
- `GET /disks/{id}` — карточка диска.
  - если диск не найден: `404` + `application/problem+json`.

### SMART

- `GET /disks/{id}/smart` — история SMART-метрик по диску (до 500 последних записей).
  - если диск не найден: `404` + `application/problem+json`.

### Архивация

- `GET /backups` — список задач архивации.
- `POST /backups` — создание задачи.
- `PUT /backups/{id}` — обновление задачи.
- `DELETE /backups/{id}` — удаление задачи (`404` + problem, если не найдена).
- `POST /backups/{id}/run` — ручной запуск задачи.
- `POST /backups/test-mssql` — проверка подключения к SQL Server без сохранения задачи. Тело: объект как `msSql` ниже (`server`, `database`, `auth`: `sql` | `windows`, `user`, `password`, `trustServerCertificate`).

### Логи архивации

- `GET /backups/logs` — журнал выполнения.
  - опциональные query-параметры: `status` (`Succeeded` | `Failed`), `from`, `to` (ISO-8601 `DateTimeOffset`).

## Типы задач архивации

- `file`:
  - `source` — абсолютный путь к файлу базы (`.1CD`);
  - `destination` — папка для архивов.
- `msSql`:
  - либо `source` — полная строка подключения;
  - либо объект **`msSql`**: при его наличии строка подключения собирается на сервере и записывается в `source`:
    - `server`, `database`, `auth` (`sql` | `windows`), при `sql` — `user`, `password`, `trustServerCertificate` (по умолчанию `true`).

## Валидация запросов архивации

- Обязательные поля, cron Quartz, `retentionCount > 0`.
- Файловый сценарий: абсолютные пути, файл источника существует, нет конфликта путей.
- MSSQL: проверка подключения и базы перед сохранением.
- Папка назначения: абсолютный путь, тест записи.

## Статические файлы панели (SPA)

- Если рядом с `AriaSignature.Service.exe` развёрнута папка `wwwroot`, служба отдаёт интерфейс по корню URL (например `http://127.0.0.1:5160/`).
- Маршруты `/api/v1/*` и `/swagger` не перекрываются SPA.

## Формат ошибок

- Валидация: `400`, validation problem details.
- Not found: `404`, `application/problem+json`.
- Прочие: `500`, `application/problem+json`, `traceId`.

## OpenAPI

- Swagger UI: `/swagger`

## Примечания

- API работает локально; для внешних систем при необходимости настраиваются правила брандмауэра/прокси отдельно.
- Метаданные и журналы — SQLite (`ConnectionStrings:AriaSignature`); это не база 1С и не MSSQL.
- Архивы по возможности упаковываются в `.rar` (наличие Rar.exe влияет на успех шага упаковки).
