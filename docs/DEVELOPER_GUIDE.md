# AriaSignature — developer guide

Версия документа: 0.2.2.

## 1. Цель системы

AriaSignature реализует `service-first` модель:
- локальный сервис собирает телеметрию и выполняет задачи архивации;
- desktop UI предоставляет операторский интерфейс;
- API публикует данные сервиса для внешних систем.

Система должна работать автономно после настройки: автозапуск, плановое выполнение, API-доступность.

## 2. Архитектурные границы

- `AriaSignature.Domain` — сущности и бизнес-перечисления.
- `AriaSignature.Application` — use-case сервисы и абстракции.
- `AriaSignature.Infrastructure` — реализация хранилища, телеметрии, backup execution.
- `AriaSignature.Api` — HTTP-контракт и API host.
- `AriaSignature.Service` — Windows service host.
- `AriaSignature.UI` — WPF shell + WebView2.
- `src/web` — React/Vite SPA.

Контракт API: `/api/v1`.

## 3. Диагностика дисков

Точка входа: `IDiskTelemetryCollector`.

Текущая реализация агрегирует:
- WMI (`Win32_DiskDrive`, `MSStorageDriver_*`);
- Storage counters (`MSFT_StorageReliabilityCounter`);
- low-level SMART/NVMe через `smartctl` (если доступен).

Правило слияния: выбираются наиболее информативные значения, идентификация выполняется по model/serial и физическим индексам, где возможно.

История срезов и SMART хранится в SQLite через `IDiskTelemetryRepository`.

## 4. Архивация

Точка входа: `IBackupService`.

Поддерживаемые сценарии:
- `file` (бэкап `.1CD`);
- `msSql` (backup SQL Server).

Функционал:
- CRUD задач;
- ручной запуск;
- плановое выполнение;
- retention;
- журналирование;
- валидация входных параметров и окружения.

## 5. UI и host взаимодействие

- SPA (`src/web`) использует API сервиса.
- WPF host реализует:
  - запуск/проверку службы;
  - системный трей;
  - автозапуск;
  - файловые/папочные диалоги;
  - управление сервисом через postMessage bridge.

Ссылки из WebView открываются во внешнем браузере по умолчанию.

## 6. Версионирование (обязательная синхронизация)

При изменении версии обновлять одним коммитом:
- `Directory.Build.props`;
- `installer/inno/AriaSignature.iss` (`MyAppVersion`);
- `src/web/package.json` и lockfile;
- `docs/API.md` и `docs/USER_GUIDE.md`.

Изменения релиза фиксировать в `docs/DEVELOPMENT_NOTES.md`.

## 7. Стандарты документации

Обязательные требования:
- документация обновляется вместе с изменением контракта/поведения;
- описывается фактическое состояние системы, без плановых формулировок;
- используется единый технический стиль: кратко, предметно, проверяемо.

Минимальный набор при каждом изменении:
- пользовательское воздействие (`USER_GUIDE.md`);
- API-контракт (`API.md`);
- техническая реализация/процесс (`DEVELOPER_GUIDE.md`, при необходимости `INSTALLER.md`, `RELEASE_GATE.md`);
- запись в `DEVELOPMENT_NOTES.md`.

## 8. Сборка и проверка

Основной gate:
- `.\scripts\release-gate.ps1`

Gate выполняет:
- сборку SPA;
- build/test .NET решения;
- publish UI/service;
- сборку Inno Setup installer.

Артефакт: `artifacts/installer/AriaSignature-Setup.exe`.
