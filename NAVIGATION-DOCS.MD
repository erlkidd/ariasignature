# Навигация по документации AriaSignature

Версия продукта: **1.1.2**

Единый указатель актуальных документов репозитория. Техническая спецификация REST — [`docs/API.md`](docs/API.md); карта handler keys Melezh — [`docs/MELEZH_HANDLER_CATALOG.md`](docs/MELEZH_HANDLER_CATALOG.md).

## Быстрый старт

| Документ | Для кого |
|----------|----------|
| [`README.md`](README.md) | Обзор продукта, сборка, ссылки |
| [`docs/USER_GUIDE.md`](docs/USER_GUIDE.md) | Оператор: установка, UI, службы, WinRAR |
| [`docs/INSTALLER.md`](docs/INSTALLER.md) | Сборка и установщик Inno Setup |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Участие в разработке, ветки, gate |

## API и интеграции

| Документ | Содержание |
|----------|------------|
| [`docs/API.md`](docs/API.md) | REST `/api/v1`, авторизация, Melezh vs :5160, матрица покрытия UI/sync |
| [`docs/MELEZH.md`](docs/MELEZH.md) | Служба `AriaSignatureMelezhService`, порты, repair, UI |
| [`docs/MELEZH_HANDLER_CATALOG.md`](docs/MELEZH_HANDLER_CATALOG.md) | Каталог `aria_*` ↔ API (bootstrap v6) |
| [`docs-opi-melezh/`](docs-opi-melezh/) | Справка OInt/Melezh (внешний продукт, не дублировать в `docs/`) |

Проверки контракта: `scripts/Test-ApiDocParity.ps1`, `scripts/Test-MelezhCatalogParity.ps1`, unit-тесты в `tests/AriaSignature.UnitTests/`.

## Разработка и релиз

| Документ | Содержание |
|----------|------------|
| [`docs/DEVELOPER_GUIDE.md`](docs/DEVELOPER_GUIDE.md) | Архитектура, модули, версионирование, SCM-матрица |
| [`docs/RELEASE_GATE.md`](docs/RELEASE_GATE.md) | `release-gate.ps1`, артефакты |
| [`docs/VERSIONING.md`](docs/VERSIONING.md) | `VERSION`, `sync-project-version.ps1` |
| [`docs/AI_CONTEXT.md`](docs/AI_CONTEXT.md) | Краткий контекст для ИИ-агентов |
| [`docs/CHANGELOG.md`](docs/CHANGELOG.md) | История изменений |

## Эксплуатация

| Документ | Содержание |
|----------|------------|
| [`docs/OPERATIONS_RUNBOOK.md`](docs/OPERATIONS_RUNBOOK.md) | Runbook, triage, recovery |
| [`docs/TELEMETRY_HEALTH.md`](docs/TELEMETRY_HEALTH.md) | DiskHealthEngine, health в API |
| [`docs/incidents/`](docs/incidents/) | Разбор инцидентов (SCM, startup, stability) |

## Устаревшие / удалённые

- `docs/INSTRUCTIONS.md`, `docs/DEVELOPMENT_NOTES.md` — объединены в DEVELOPER_GUIDE / CHANGELOG.
- `docs/TZ_1C_EXTENSION_AriaSignature_LT.md` — удалён (не актуален для текущей линии).
