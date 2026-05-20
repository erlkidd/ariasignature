# Инцидент: стабильность продукта (UI, installer, диски, Melezh sync)

Версия: 1.1.x. Связанные документы: [`INCIDENT_STARTUP_W10_W11.md`](INCIDENT_STARTUP_W10_W11.md), [`INCIDENT_SCM_INSTALL_UNINSTALL_MELEZH.md`](INCIDENT_SCM_INSTALL_UNINSTALL_MELEZH.md).

## Симптомы и исправления

| Симптом | Причина | Что сделано |
|---------|---------|-------------|
| Белый экран при запуске UI | Overlay снимался по таймауту 8 с до `appReady` из WebView | Overlay до сообщения `action=appReady`; API :5160 поднимается через recovery loop |
| Долгая установка / uninstall | Ожидание `sc stop`, копирование WebView2 ~100 MB, MsgBox при uninstall | `FastRemoveServiceBestEffort`, WebView2 `[Files]` только при `NeedsWebView2Runtime`, uninstall без MsgBox, `RemoveAppDirectoryBestEffort` |
| HDD отображается как SSD | Сырой Win32 `MediaType` без нормализации | `DiskTelemetryRules.NormalizeMediaType`, приоритет `MSFT_PhysicalDisk` |
| «Ресурс SSD» 0% без wear | Fallback `healthPercent` в UI | `ssdLife` → `null` без подтверждённого износа; UI не подставляет SMART-оценку |
| Нет POST в Melezh | Только чтение :5160 для 1С | `MelezhSyncJob`, `POST /api/v1/melezh/push`, handlers `aria_ping` / `aria_sync` |
| Handler Melezh не дергает :5160 | Нет URL/токена в аргументах, cron только на static GET, API не слушает | Bootstrap `MelezhProjectBootstrap`, каталог `docs/MELEZH_HANDLER_CATALOG.md`; проверить `aria_get_status` и порт `Api:Port` |
| `%ProgramData%\AriaSignature` удалялся | — | **Не удаляем** при uninstall (логи, SQLite, melezh project) |

## Проверка после исправления

1. Uninstall: нет `{app}`, службы сняты, ProgramData сохранён.
2. Install: WebView2 skip если установлен; Finish без зависания.
3. UI: панель с overlay до готовности React.
4. Диски: HDD/SSD/NVMe корректно; ресурс SSD без ложного 0%.
5. `GET /api/v1/settings` — поля `melezhSync*`; `GET http://127.0.0.1:7788/aria_ping` после старта Melezh.

## Эксплуатация Melezh sync

- Настройки: `melezhSyncEnabled`, `melezhSyncHandler` (по умолчанию `aria_sync`), `melezhSyncCron` (`0 0/15 * * * ?`).
- Ручной push: `POST /api/v1/melezh/push`.
- Тело POST в Melezh совпадает со схемой исходящей синхронизации (`timestampUtc`, `system`, `disks`, `backups`).
