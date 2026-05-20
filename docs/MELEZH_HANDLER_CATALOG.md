# Melezh handler catalog (AriaSignature API bridge)

Версия документа: 1.1.1

Источник правды в коде: `src/service/AriaSignature.MelezhHost/MelezhAriaApiHandlerCatalog.cs`.  
При старте `AriaSignatureMelezhService` выполняется идемпотентный bootstrap (`MelezhProjectBootstrap`).

## Инварианты

| Правило | Описание |
|---------|----------|
| 1 endpoint = 1 handler | Один стабильный key на `:7788` на каждую пару HTTP-метод + путь Aria API |
| Inbound push | Только `aria_sync` принимает JSON от агента (`MelezhSyncJob` → `POST :7788/aria_sync`). Outbound handler на `POST /api/v1/melezh/push` **не** создаётся |
| Планировщик Melezh | Только **GET** без `{id}` в пути; cron по умолчанию `0 */5 * * * * *` (`ARIASIGNATURE_MELEZH_PULL_CRON`) |
| POST/PUT/DELETE outbound | Handlers есть, cron **нет** — вызов вручную или из 1С через `:7788/<key>` |
| OInt `http` | Поддерживаются **GET** и **POST**; `Put`/`Delete` в библиотеке **не работают** — PUT/DELETE handlers зарегистрированы с `Post`/`json` (см. ограничение ниже) |

Базовый URL в аргументах handler: `http://127.0.0.1:{port}/api/v1` (`ARIASIGNATURE_API_PORT` или `Api:Port` из `%ProgramData%\AriaSignature\ariasignature.db`). При заданном `Api:SharedSecret` bootstrap добавляет заголовки `Authorization` / `X-Aria-Api-Key`.

## Inbound (приём на :7788)

| Handler key | Melezh method | Назначение |
|-------------|---------------|------------|
| `aria_ping` | GET | Health / диагностика |
| `aria_sync` | POST (JSON) | Снимок телеметрии от `AriaSignatureService` и внешних POST |

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

**Ограничение OInt:** для `PUT`/`DELETE` маршрутов Aria API handler вызывает `http`/`Post`/`json`; настоящий REST PUT/DELETE к `:5160` через стандартную библиотеку `http` недоступен. Для критичных изменений используйте прямой вызов API агента или доработку OInt-скрипта.

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
