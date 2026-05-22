# AriaSignature — контекст для ИИ-агентов

Версия документа: 1.1.2

Полная карта документации: [`NAVIGATION-DOCS.MD`](../NAVIGATION-DOCS.MD).

## Система

- `AriaSignature.Service` — Windows Service, API **5160**, SQLite, Quartz jobs.
- `AriaSignatureMelezhService` + `AriaSignature.MelezhHost` — Melezh **7788**, bootstrap каталога `aria_*`.
- `AriaSignature.UI` — WPF + WebView2; SPA в `src/web` → `wwwroot` службы.

Принцип: данные и фоновые задачи в службе; UI и удалённые клиенты читают **один** API `/api/v1`.

## Инварианты

- Служба стартует без UI; `GET /api/v1/status` доступен при healthy start.
- Изменения поведения → `docs/CHANGELOG.md`.
- API routes ↔ `docs/API.md` ↔ `Test-ApiDocParity.ps1`.
- Melezh handlers ↔ `MelezhAriaApiHandlerCatalog.cs` ↔ `docs/MELEZH_HANDLER_CATALOG.md`.

## Диагностика

- Логи: `%ProgramData%\AriaSignature\logs\service-*.log`, `melezh-host-*.log`.
- SCM **1053** — таймаут старта; см. `docs/USER_GUIDE.md`, `docs/incidents/`.
- Runbook: `docs/OPERATIONS_RUNBOOK.md`.

## Разработка

- Архитектура и gate: `docs/DEVELOPER_GUIDE.md`, `docs/RELEASE_GATE.md`.
- Ветки: `test/agent-work` → `production`; remotes `origin`, `old-origin`.
