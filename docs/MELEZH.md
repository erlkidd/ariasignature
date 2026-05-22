# Melezh в составе AriaSignature

Версия документа: 1.1.2


## Назначение

**Melezh** — HTTP-шлюз над **OpenIntegrations (OInt)** для внешних интеграций: Telegram, HTTP, почта, БД и др.

**Источник данных** — API агента (`AriaSignatureService`, `:5160`). **Обязательный push** снимка в шлюз: Quartz `melezh-sync-job` / `POST :7788/aria_sync`. **Внешние потребители** (1С, OInt) читают агент **через Melezh** (`GET :7788/aria_get_*`); прямой `GET :5160/api/v1/*` допустим для UI и диагностики, но не заменяет шлюз. См. [`docs/API.md`](API.md) § 3.4 и [`docs/MELEZH_HANDLER_CATALOG.md`](MELEZH_HANDLER_CATALOG.md).

## Компоненты на ПК клиента

| Компонент | Порт / имя | Роль |
|-----------|------------|------|
| `AriaSignatureService` | 5160 | API агента, архивации, телеметрия |
| `AriaSignatureMelezhService` | 7788 | Melezh HTTP + Web UI |
| `AriaSignature.UI` | — | Панель оператора |

## Установка

При установке AriaSignature:

1. Копируется runtime **OInt** (включая Melezh) в `{app}\melezh\` из `installer/melezh/bundle/` (см. `oint_*_installer_ru.exe` + `scripts/prepare-melezh.ps1`).
2. Регистрируется служба **`AriaSignatureMelezhService`** (`start= auto`).
3. При первом старте создаётся проект `%ProgramData%\AriaSignature\melezh\AriaSignature.melezh`.
4. Открывается firewall для TCP **7788**.

Подготовка bundle для сборки installer: `scripts/prepare-melezh.ps1` (см. `installer/melezh/README.md`).

## Web UI

- URL: `http://127.0.0.1:7788/ui`
- Ссылка также доступна в панели AriaSignature: **Настройки → Melezh / OpenIntegrations**

При первом старте `AriaSignature.MelezhHost` создаёт проект и выполняет bootstrap (`MelezhProjectBootstrap`): **один handler key на каждый endpoint API** (см. [`docs/MELEZH_HANDLER_CATALOG.md`](MELEZH_HANDLER_CATALOG.md)).

| Канал | Примеры handler | Назначение |
|-------|-----------------|------------|
| Inbound `:7788` | `aria_ping`, `aria_sync` | Health и приём JSON от агента (`melezhSync*`) |
| Outbound GET (cron Melezh) | `aria_get_disks`, `aria_get_status`, … | Планировщик Melezh опрашивает `GET http://127.0.0.1:5160/api/v1/...` (только static GET, без `{id}`) |
| Outbound POST/PUT/DELETE | `aria_post_backups`, `aria_put_settings`, … | Без cron; вызов вручную или из 1С через `:7788/<key>` |

Push от агента: `MelezhSyncJob` → `POST :7788/aria_sync`. Отдельный outbound handler на `POST /api/v1/melezh/push` **не** создаётся.

Cron pull (bootstrap v5+): для каждого из 10 `aria_get_*` — **своя** секунда в цикле 5 минут (`0 */5`, `4 */5`, `8 */5`, … `36 */5`), чтобы не гонять общий OInt `http` параллельно. Переопределение всех pull одним выражением: env `ARIASIGNATURE_MELEZH_PULL_CRON`. Push от агента по умолчанию: Quartz `20 2/15 * * * ?` (настройка `melezhSyncCron`). Это **не** заменяет `melezh-sync-job`; оба канала в `docs/API.md`.

### Параллельные cron и OInt HTTP

Если в Web UI Melezh «краснеют» десятки handler’ов с `InvalidOperationException` / `non-concurrent collections` в `OPI_HTTPКлиент.os`, причина обычно **одновременные** вызовы модуля `http`, а не отдельные ошибки конфигурации. После bootstrap v4 все pull могли срабатывать в **одну секунду** (`0 */5 * * * * *`).

| Действие | Команда |
|----------|---------|
| Repair + stagger + prune дубликатов | `.\scripts\repair-melezh.ps1` (admin) |
| Только bootstrap | `AriaSignature.MelezhHost.exe --bootstrap-only` |
| Проверка | `GET :7788/aria_ping`, `POST :7788/aria_sync` с `{}` |

`aria_sync` в Web UI Melezh по ссылке (GET) → **Method Not Allowed**; нужен **POST** + JSON (кнопка в панели Aria или `POST /api/v1/melezh/push`).

## Связка 1С ↔ AriaSignature ↔ Melezh

```text
1С (расширение)
  ├─ GET http://<agent-ip>:5160/api/v1/disks|backups|...   ← данные агента
  └─ POST http://<agent-ip>:7788/<handler>                 ← интеграции OInt через Melezh

Периодическая отправка снимка с агента в Melezh (тот же JSON, что исходящий sync): настройки `melezhSync*` в `GET/PUT /api/v1/settings`, ручной push — `POST /api/v1/melezh/push`. См. `docs/API.md`.
```

## CLI (OInt 0.12+ / Melezh 0.12)

Служба `AriaSignature.MelezhHost` запускает Melezh через `oscript.exe` и `app.os` с **русскими** именами методов:

```powershell
# из {app}\melezh\lib\oint\bin\oscript.exe + share\...\app.os
СоздатьПроект --path C:\ProgramData\AriaSignature\melezh\AriaSignature.melezh
ЗапуститьПроект --port 7788 --proj C:\ProgramData\AriaSignature\melezh\AriaSignature.melezh
```

Английские `CreateProject` / `RunProject` в CLI **не работают** (exit code **99**, «неизвестный параметр --path»). Имена `CreateProject`/`RunProject` в IntegrationProxy — только для programmatic API, не для командной строки.

Проверка bundle при сборке: `scripts\Test-MelezhCli.ps1`.

## Диагностика

| Симптом | Действие |
|---------|----------|
| Web UI недоступен, служба Running | `.\scripts\diagnose-melezh.ps1` (admin); лог `melezh-host-*.log` |
| В логе `CreateProject` / `code=99` / «неизвестный параметр --path» | Обновить `melezh-host\AriaSignature.MelezhHost.exe` (1.1.0+ с русскими CLI) и перезапустить службу |
| `GET :7788/aria_ping` → `result:false`, «Некорректное имя команды: http» | Пересобрать bundle (`prepare-melezh` + патч `oint-http-index`), `repair-melezh.ps1` или `MelezhHost --bootstrap-only` (bootstrap v2+ пересоздаёт handlers) |
| Массовые 500, `non-concurrent collections` на многих handler’ах | `repair-melezh.ps1` (bootstrap **v5**: stagger cron, prune лишних keys), перезапуск `AriaSignatureMelezhService` |
| `aria_sync` → Method Not Allowed в Web UI Melezh | Не открывать GET; только POST (`/api/v1/melezh/push` или `POST :7788/aria_sync`) |
| Web UI недоступен | `services.msc` → `AriaSignatureMelezhService` → перезапуск |
| Служба не стартует | `%ProgramData%\AriaSignature\logs\melezh-host-*.log` |
| Нет `bin\melezh.bat` | Переустановить сборку с полным OInt bundle (`prepare-melezh`) |
| После установки нет блока Melezh в UI | API отдаёт старую версию — `.\scripts\repair-upgrade.ps1` или переустановите 1.1.0; проверьте `/api/v1/status` |
| Ошибка «файлы/API не обновились до 1.1.0» | Закройте AriaSignature, перезагрузите ПК, снова setup **или** `.\scripts\repair-upgrade.ps1` (admin) |
| Служба не зарегистрирована | PowerShell (admin): `.\scripts\repair-melezh.ps1` или **Настройки → Восстановить службу Melezh** |

## Восстановление (repair)

```powershell
# от имени администратора, из корня репозитория
# полный upgrade-repair (служба AriaSignature + проверка версии API + Melezh):
.\scripts\repair-upgrade.ps1

# только Melezh:
.\scripts\repair-melezh.ps1
```

## Проверка моста

| Вызов | Метод на :7788 | Назначение |
|-------|----------------|------------|
| `/aria_get_*` | **GET** | Прокси GET к `:5160` |
| `/aria_post_*`, `/aria_sync` | **POST** + JSON | Прокси POST (и PUT/DELETE через OInt) к `:5160` |
| `/aria_ping` | **GET** | Health |

1. `GET http://127.0.0.1:7788/aria_get_disks`
2. `POST http://127.0.0.1:7788/aria_post_disks_refresh` с телом `{}`
3. `POST http://127.0.0.1:7788/aria_sync` — тело как у `POST /api/v1/melezh/push`
4. `POST http://127.0.0.1:7788/aria_delete_backups_logs` с телом `{}`

Автоматизация: `scripts/Test-MelezhHandlerBootstrap.ps1`, `scripts/Test-MelezhAriaBridge.ps1`, `scripts/Test-MelezhWriteHandlers.ps1` (release-gate).

## Дополнительная документация

- Каталог handlers: [`docs/MELEZH_HANDLER_CATALOG.md`](MELEZH_HANDLER_CATALOG.md)
- Локальное зеркало: `docs-opi-melezh/melezh/`
- OpenIntegrations: https://en.openintegrations.dev/docs/Addons/Melezh/Start/Installation/
- Инцидент SCM/install/uninstall + autostart: [`docs/incidents/INCIDENT_SCM_INSTALL_UNINSTALL_MELEZH.md`](incidents/INCIDENT_SCM_INSTALL_UNINSTALL_MELEZH.md)
- Каталог handler keys: [`MELEZH_HANDLER_CATALOG.md`](MELEZH_HANDLER_CATALOG.md) (источник правды в коде — `MelezhAriaApiHandlerCatalog.cs`)
