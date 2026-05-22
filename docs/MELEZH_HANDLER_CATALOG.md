# Melezh handler catalog (AriaSignature API bridge)

Версия документа: 1.1.2

Источник правды в коде: `src/service/AriaSignature.MelezhHost/MelezhAriaApiHandlerCatalog.cs`.  
При старте `AriaSignatureMelezhService` выполняется идемпотентный bootstrap (`MelezhProjectBootstrap`, schema **v7**: стабильные ключи `aria_*` по `rowid` после CLI add, prune GUID-сирот на каждом ensure, cron только для static GET).

## Инварианты

| Правило | Описание |
|---------|----------|
| 1 endpoint = 1 handler | Один стабильный key на `:7788` на каждую пару HTTP-метод + путь Aria API |
| Inbound push | Только `aria_sync` принимает JSON от агента (`MelezhSyncJob` → `POST :7788/aria_sync`). Outbound handler на `POST /api/v1/melezh/push` **не** создаётся |
| Планировщик Melezh | Только **GET** без `{id}` в пути; cron по умолчанию `0 */5 * * * * *` (`ARIASIGNATURE_MELEZH_PULL_CRON`) |
| POST/PUT/DELETE outbound | Handlers есть, cron **нет** — вызов вручную или из 1С через `:7788/<key>` |
| OInt `http` | CLI-индекс `http`/`Get`/`PostСТелом`/`PutСТелом`/`DeleteСТелом`; tokens **GET** / **JSON** на шлюзе |

Базовый URL в аргументах handler: `http://127.0.0.1:{port}/api/v1` (`ARIASIGNATURE_API_PORT` или `Api:Port` из `%ProgramData%\AriaSignature\ariasignature.db`). При заданном `Api:SharedSecret` bootstrap добавляет заголовки `Authorization` / `X-Aria-Api-Key`.

## Inbound (приём на :7788)

| Handler key | Melezh method | Назначение |
|-------------|---------------|------------|
| `aria_ping` | GET | Health / диагностика |
| `aria_sync` | POST (JSON) | Снимок телеметрии от `AriaSignatureService` (`http` / `PostСТелом` → `POST :5160/api/v1/melezh/ingest` ACK) |

## Outbound GET (опрос :5160)

| Handler key | Aria API | Scheduler (по умолчанию) |
|-------------|----------|--------------------------|
| `aria_get_status` | `GET /status` | да |
| `aria_get_health_live` | `GET /health/live` | да |
| `aria_get_health_ready` | `GET /health/ready` | да |
| `aria_get_health_degradation` | `GET /health/degradation` | да |
| `aria_get_observability_runtime` | `GET /observability/runtime` | да |
| `aria_get_settings` | `GET /settings` | да |
| `aria_get_system` | `GET /system` | да |
| `aria_get_disks` | `GET /disks` | да |
| `aria_get_backups` | `GET /backups` | да |
| `aria_get_backups_logs` | `GET /backups/logs` | да |
| `aria_get_disk` | `GET /disks/{diskId}` | нет (нужен `diskId` в URL) |
| `aria_get_disk_smart` | `GET /disks/{diskId}/smart` | нет |

### Handler’ы с `{diskId}` / `{backupId}` (placeholder в bootstrap)

При создании проекта bootstrap подставляет GUID-заглушку `00000000-0000-0000-0000-000000000001` в URL аргументов handler’а. Проверка `aria_get_disk_smart` из Web UI Melezh **без замены id** проксирует запрос к API с этим GUID → ответ **404 «Диск не найден»** — это норма, не ошибка установки.

Правильный сценарий: `aria_get_disks` → взять `id` реального диска → вызвать handler с подставленным id (1С, скрипт, ручной URL).

## Outbound POST / PUT / DELETE

| Handler key | Aria API | Scheduler |
|-------------|----------|-----------|
| `aria_post_backups_test_mssql` | `POST /backups/test-mssql` | нет |
| `aria_post_disks_refresh` | `POST /disks/refresh` | нет |
| `aria_post_backups` | `POST /backups` | нет |
| `aria_post_backup_run` | `POST /backups/{backupId}/run` | нет |
| `aria_put_settings` | `PUT /settings` | нет |
| `aria_put_backup` | `PUT /backups/{backupId}` | нет |
| `aria_delete_disk_smart` | `DELETE /disks/{diskId}/smart` | нет |
| `aria_delete_disks_smart` | `DELETE /disks/smart` | нет |
| `aria_delete_backup` | `DELETE /backups/{backupId}` | нет |
| `aria_delete_backups_logs` | `DELETE /backups/logs` | нет |

**HTTP на :7788 vs :5160:** клиент вызывает handler на Melezh методом **POST** + `Content-Type: application/json` (тип handler `JSON`). Melezh выполняет OInt `http` с нужным глаголом к API: `PostСТелом` / `PutСТелом` / `DeleteСТелом` → `POST` / `PUT` / `DELETE` на `:5160`. Ошибка `"Method Not Allowed"` у `aria_sync` в Web UI — вызов **GET** вместо POST; у outbound write — устаревший bootstrap (repair v4+).

**Pull cron (bootstrap v5+):** каждый scheduled `aria_get_*` — отдельная секунда (`0`, `4`, `8`, … `36` в шаблоне `N */5 * * * * *`).

**Repair v7:** при `BootstrapVersion` &lt; 7 или «дрейфе» каталога (GUID-ключи, пропущенные `aria_*`) — полный repair: prune, пересоздание handler’ов, `scheduler_tasks`. `bootstrap-only` только при **остановленной** службе Melezh. Команды: `repair-melezh.ps1`, `MelezhHost --bootstrap-only`, перезапуск `AriaSignatureMelezhService`.

## Переменные окружения (bootstrap / host)

| Переменная | Назначение |
|------------|------------|
| `ARIASIGNATURE_MELEZH_ROOT` | Корень OInt/Melezh (dev/tests: `installer/melezh/bundle`) |
| `ARIASIGNATURE_MELEZH_PROJECT` | Путь к `.melezh` (по умолчанию `%ProgramData%\AriaSignature\melezh\AriaSignature.melezh`) |
| `ARIASIGNATURE_MELEZH_PULL_CRON` | Cron планировщика Melezh для GET static (7 полей) |
| `ARIASIGNATURE_MELEZH_PORT` | Порт HTTP Melezh (7788) |
| `ARIASIGNATURE_API_PORT` | Порт API агента для URL в handler args |

## Проверка

```powershell
.\scripts\Test-MelezhHandlerBootstrap.ps1 -MelezhRoot .\installer\melezh\bundle
.\scripts\Test-MelezhAriaBridge.ps1 -MelezhRoot .\installer\melezh\bundle
```

Ручная матрица: `docs/MELEZH.md` (раздел «Проверка моста»).
