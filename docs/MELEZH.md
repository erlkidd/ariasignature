# Melezh в составе AriaSignature

Версия документа: 1.1.0.


## Назначение

**Melezh** — HTTP-шлюз над **OpenIntegrations (OInt)** для внешних интеграций: Telegram, HTTP, почта, БД и др.

**Важно:** сбор телеметрии AriaSignature для 1С выполняется **напрямую** через API агента (`http://<host>:5160/api/v1/...`), без Melezh. Melezh нужен для исходящих интеграций (см. ЛТ-13 в `docs/TZ_1C_EXTENSION_AriaSignature_LT.md`).

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

При первом старте `AriaSignature.MelezhHost` создаёт проект и по умолчанию регистрирует handlers:

| URL path | OInt | Назначение |
|----------|------|------------|
| `http://127.0.0.1:7788/aria_ping` | `http` / `Get` (GET) | Проверка связи (используется диагностикой агента) |
| `http://127.0.0.1:7788/aria_sync` | `http` / `Post` (JSON) | Приём снимка телеметрии от `AriaSignatureService` |

Дополнительные handlers настраиваются в Web UI.

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

## Дополнительная документация

- Локальное зеркало: `docs-opi-melezh/melezh/`
- OpenIntegrations: https://en.openintegrations.dev/docs/Addons/Melezh/Start/Installation/
- Инцидент SCM/install/uninstall + autostart: `docs/INCIDENT_SCM_INSTALL_UNINSTALL_MELEZH.md`
