# AriaSignature — спецификация API (v1)

Версия документа: 1.1.2

## 1. Общие параметры

- **Префикс REST:** `/api/v1`
- **Порт по умолчанию:** `5160` (настраивается в `GET/PUT /settings`, вступает в силу после перезапуска службы).
- **Базовый URL с панели на том же ПК:** `http://127.0.0.1:{port}/api/v1`
- **Базовый URL с другой машины (VPN, LAN):** `http://{IP_или_DNS_агента}:{port}/api/v1`

По умолчанию служба слушает **все интерфейсы** (`Api:Bind = all`), поэтому запросы по IP узла, где установлен агент, доходят до API при открытом порту в **брандмауэре** и маршрутизации сети (Radmin VPN, ZeroTier и т.п.). Режим только localhost: `Api:Bind = loopback` (см. §3.2).

- Формат: `application/json`
- OpenAPI/Swagger: `/swagger` на том же хосте и порте, что и API

Источник данных — `AriaSignatureService` (телеметрия дисков, архивация, журнал). Эндпоинты отдают те же сущности, что видит локальная панель (вкладки «О системе», «Диски», «Архивация»).

### 1.1 Быстрые примеры (удалённый опрос)

Подставьте IP агента в VPN/LAN и при необходимости заголовок авторизации (если в настройках задан `apiSharedSecret`):

```http
GET http://192.168.1.50:5160/api/v1/status
GET http://192.168.1.50:5160/api/v1/system
GET http://192.168.1.50:5160/api/v1/disks
GET http://192.168.1.50:5160/api/v1/backups
GET http://192.168.1.50:5160/api/v1/backups/logs
```

С токеном:

```http
GET http://192.168.1.50:5160/api/v1/system
Authorization: Bearer <ваш_токен_из_настроек>
```

или

```http
X-Aria-Api-Key: <ваш_токен_из_настроек>
```

Запросы **с самого агента** на `127.0.0.1` заголовок не требуют, даже если токен задан (удобство локальной панели).

### 1.2 Связь с вкладкой «О системе»

Ответ **`GET /system`** совпадает по смыслу с данными вкладки **«О системе»** в UI: хост, DNS, сетевые адреса, ОС, процессор, ОЗУ, видеокарты, время сбора, версия агента.

## 2. Контракт ошибок

- `400 Bad Request` — валидация, `application/problem+json`
- `401 Unauthorized` — для запросов **не с loopback**, если задан токен и заголовок отсутствует или неверен
- `404 Not Found` — отсутствующий ресурс, `application/problem+json`
- `500 Internal Server Error` — непредвиденная ошибка, `application/problem+json` + `traceId`

## 3. Эндпоинты

### 3.1 Статус сервиса

- `GET /status`
  - Назначение: проверка доступности и версии сервиса.
  - Ответ: `status`, `timestampUtc`, `version`.

### 3.2 Настройки

- `GET /settings`
  - Назначение: чтение текущих параметров.
  - Поля:
    - `apiPort`, `apiBind` (`all` | `loopback`), `apiSharedSecret` (`string`, может быть пустым);
    - `smartMonitoringCron`, `note`;
    - `outboundSyncEnabled` (`boolean`);
    - `outboundSyncUrl` (`string`, полный URL коллектора `http`/`https`);
    - `outboundSyncCron` (Quartz cron для исходящего `POST`).
    - `melezhSyncEnabled`, `melezhSyncHandler` (по умолчанию `aria_sync`), `melezhSyncCron`;
    - `melezhEnabled`, `melezhPort`, `melezhUiUrl`, `melezhServiceName`, `melezhServiceStatus`, `melezhRunning` (диагностика службы Melezh; см. `docs/MELEZH.md`);
    - read-only: `melezhSyncLastOk`, `melezhSyncLastError`, `melezhLastError`, `melezhLogHint` (путь к последнему `melezh-host-*.log`).
  - Поле `note`: смена **порта** и **apiBind** вступает в силу после перезапуска службы `AriaSignatureService`; токен удалённого API и cron-поля применяются сразу после `PUT /settings`.

- `PUT /settings`
  - Назначение: обновление параметров.
  - Тело (все поля необязательны; отсутствующие не меняются):
    - `apiPort` (`1..65535`);
    - `apiBind` (`all` | `loopback`);
    - `apiSharedSecret` (`string`; пустая строка сбрасывает токен; поле отсутствует — не менять);
    - `smartMonitoringCron` (Quartz);
    - `outboundSyncEnabled` (`boolean`);
    - `outboundSyncUrl` (абсолютный `http`/`https`; пустая строка допустима только если синхронизация выключена);
    - `outboundSyncCron` (Quartz);
    - `melezhSyncEnabled`, `melezhSyncHandler`, `melezhSyncCron`.

- `POST /melezh/push`
  - Назначение: немедленная отправка снимка телеметрии в Melezh (`POST http://127.0.0.1:{melezhPort}/{melezhSyncHandler}`).
  - Тело запроса: пустое или `{}`.
  - Ответ: `{ "ok": true, "targetUrl": "..." }` или `502` с `{ "ok": false, "error": "...", "targetUrl": "..." }`.

- `POST /melezh/ingest`
  - Назначение: ACK для inbound handler Melezh `aria_sync` (целевой URL в bootstrap: `POST /api/v1/melezh/ingest`). Не вызывается из панели напрямую.
  - Ответ: `{ "ok": true, "result": true }`.

**Безопасность:** при **пустом** `apiSharedSecret` любой узел, который может открыть TCP до порта API, может вызывать те же операции, что и доверенный мониторинг — ограничивайте сеть (VPN), брандмауэр и при необходимости задавайте токен. Исходящий `POST` на коллектор по-прежнему не использует поля входящего API.

### 3.3 Снимок системы

- `GET /system`
  - Назначение: сведения об узле (hostname, DNS, адреса активных интерфейсов, ОС, процессор, ОЗУ, видеокарты, время сбора, версия агента) — **как на вкладке «О системе»**.
  - Ответ: camelCase JSON (`hostName`, `networkAddresses[]`, `videoControllers[]`, …).

### 3.3a Melezh и два HTTP-порта (`:5160` vs `:7788`)

| Порт | Контракт | Swagger |
|------|----------|---------|
| **5160** | REST агента `/api/v1/*` | Да (`/swagger`) |
| **7788** | Handler keys Melezh (`/aria_ping`, `/aria_get_*`, …) | Нет (см. [`MELEZH_HANDLER_CATALOG.md`](MELEZH_HANDLER_CATALOG.md)) |

Соответствие (типовые примеры):

| Агент `:5160` | Melezh `:7788` | Примечание |
|---------------|----------------|------------|
| `POST /api/v1/melezh/push` | `POST /aria_sync` | Один JSON-снимок телеметрии |
| `GET /api/v1/disks` | `GET /aria_get_disks` | Pull по cron (stagger v5) |
| `GET /api/v1/disks/{id}/smart` | `GET /aria_get_disk_smart` | В bootstrap URL подставлен **placeholder** `00000000-0000-0000-0000-000000000001`; тест из Web UI без реального `id` даёт **404** — ожидаемо. Возьмите `id` из `aria_get_disks` / `GET /disks`. |
| `GET /api/v1/status` | `GET /aria_ping` | Health |

Внешние интеграции (1С, OInt) для чтения данных агента ориентируются на **:7788**, не на прямой `:5160`.

### 3.4 Синхронизация в Melezh (локальный POST :7788)

- Назначение: доставка того же JSON-снимка, что и раздел 3.5 исходящего sync, в handler Melezh на этой машине (интеграции OInt / 1С).
- Управление: `melezhSync*` в разделе 3.2; Quartz job `melezh-sync-job` в `AriaSignatureService`.
- URL: `http://127.0.0.1:{melezhPort}/{melezhSyncHandler}` (handler по умолчанию `aria_sync`, health `aria_ping`).
- Схема тела: как в разделе 3.5 (исходящий POST на коллектор).
- **Push в шлюз обязателен** для доставки снимка на `:7788` (Quartz + `POST /api/v1/melezh/push` → тот же handler `aria_sync`).
- **Опрос через Melezh (pull):** планировщик Melezh по cron вызывает outbound GET handlers (`aria_get_*`); прямой `GET :5160/api/v1/*` допустим для UI/диагностики, но внешние интеграции ориентируются на `:7788`. См. [`docs/MELEZH_HANDLER_CATALOG.md`](MELEZH_HANDLER_CATALOG.md).

### 3.5 Исходящая синхронизация (POST на коллектор)

- Назначение: дополнительный канал доставки данных на сервер заказчика **наружу** от машины с агентом (исходящее соединение). **Не заменяет** опрос по `GET /api/v1/*`.
- Управление: флаги и поля из раздела 3.2; выполняется службой `AriaSignatureService` по расписанию Quartz (`outbound-sync-job`).
- При выключенной синхронизации или пустом URL запросы не отправляются.
- Тело исходящего `POST` (JSON, `application/json`), обобщённая схема:
  - `timestampUtc`, `agentVersion`;
  - `system` — объект того же вида, что ответ `GET /system`;
  - `disks` — массив объектов дисков (как в `GET /disks`);
  - `backups` — объект `jobs` (как `GET /backups`) и `recentLogs` (последние записи журнала, ограниченный объём).

Заголовки запроса к коллектору: `Content-Type: application/json` (и при необходимости стандартные заголовки клиента); без `Authorization` и без пользовательских токенов из панели настроек.

### 3.6 Телеметрия дисков

- `GET /disks`
  - Назначение: список диагностируемых дисков.
  - Ответ содержит: идентификатор, модель, интерфейс, объемы, температуру, health, SMART-счетчики, статус.
  - Расширенные поля наблюдаемости: `smartCtlUsed`, `wmiUsed`, `storageReliabilityUsed`, `telemetryConfidence`, `telemetryDegradationReason`.
  - Nullable-поля телеметрии: `temperatureCelsius`, `healthPercent`, `ssdLifeRemainingPercent`.

- `POST /disks/refresh`
  - Назначение: принудительный пересчёт среза телеметрии и **запись точки в историю** (`SmartMetrics`).
  - Источники: primary `smartctl` + fallback WMI/Storage Reliability.
  - При старте службы выполняется «прогрев» списка дисков **без** добавления строк в историю, чтобы не раздувать SQLite.

- Начальное расписание опроса дисков (если в базе ещё нет ключа `SmartMonitoring:Cron`): **`0 0 * * * ?`** (раз в час). Переопределяется полем `smartMonitoringCron` в `GET/PUT /settings`.

- `GET /disks/{id}`
  - Назначение: карточка диска.
  - `404`, если диск не найден.

- `GET /disks/{id}/smart`
  - Назначение: история SMART-метрик (последние записи).
  - `404`, если диск не найден.

- `DELETE /disks/{id}/smart`
  - Назначение: очистка истории SMART только для выбранного диска.
  - Примечание: локальная операторская операция, используется из UI с подтверждением.
  - Ответ: `cleared`, `scope`, `diskId`, `deleted`.
  - `404`, если диск не найден.

- `DELETE /disks/smart`
  - Назначение: глобальная очистка SMART-истории по всем дискам.
  - Примечание: локальная операторская операция повышенного риска, выполняется только с подтверждением.
  - Ответ: `cleared`, `scope`, `deleted`.

### 3.7 Задачи архивации

В панели вкладка «Активные задачи» показывает задачи, **включённые в расписание**, и задачу, которая **выполняется в данный момент** (в том числе при снятом флажке «Вкл»).

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

### 3.8 Журнал архивации

- `GET /backups/logs`
  - Назначение: журнал выполнения задач.
  - Параметры запроса:
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
  - `trustServerCertificate` (необязательно)

## 5. Валидация

- обязательные поля;
- корректность Quartz cron;
- `retentionCount > 0`;
- абсолютные пути и доступность destination;
- для `file`: существование source;
- для `msSql`: валидность подключения и доступ к базе.

## 6. Эксплуатационные заметки

- Типовой сценарий: агенты на ПК клиентов, **центральный опрос** по HTTP с вашей стороны (`GET /system`, `GET /disks`, `GET /backups`, …) по IP в VPN/LAN без RDP.
- Дополнительно агент может **сам отправлять** JSON на ваш коллектор (`POST` наружу, см. §3.4) — каналы совместимы.
- UI и внешние интеграции используют один контракт `/api/v1`.
- Для старта локальной панели должны отвечать `GET /api/v1/status` и корневой `GET /` (SPA).
- Установщик добавляет правило брандмауэра для **TCP 5160**; при смене порта обновите правило вручную.
- Не выставляйте открытый порт API в публичный интернет без изоляции; предпочтительны частные сети и опциональный токен удалённого API.
