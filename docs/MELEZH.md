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

1. Копируются бинарники Melezh в `{app}\melezh\`.
2. Регистрируется служба **`AriaSignatureMelezhService`** (`start= auto`).
3. При первом старте создаётся проект `%ProgramData%\AriaSignature\melezh\AriaSignature.melezh`.
4. Открывается firewall для TCP **7788**.

Подготовка bundle для сборки installer: `scripts/prepare-melezh.ps1` (см. `installer/melezh/README.md`).

## Web UI

- URL: `http://127.0.0.1:7788/ui`
- Ссылка также доступна в панели AriaSignature: **Настройки → Melezh / OpenIntegrations**

В Web UI настраиваются handlers, аргументы и логи Melezh.

## Связка 1С ↔ AriaSignature ↔ Melezh

```text
1С (расширение)
  ├─ GET http://<agent-ip>:5160/api/v1/disks|backups|...   ← данные агента
  └─ POST http://<agent-ip>:7788/<handler>                 ← интеграции OInt через Melezh
```

## Диагностика

| Симптом | Действие |
|---------|----------|
| Web UI недоступен | `services.msc` → `AriaSignatureMelezhService` → перезапуск |
| Служба не стартует | `%ProgramData%\AriaSignature\logs\melezh-host-*.log` |
| Нет melezh.exe | Переустановить сборку с полным installer bundle |

## Дополнительная документация

- Локальное зеркало: `docs-opi-melezh/melezh/`
- OpenIntegrations: https://en.openintegrations.dev/docs/Addons/Melezh/Start/Installation/
