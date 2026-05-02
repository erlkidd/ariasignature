# AriaSignature — руководство разработчика

Версия документа: 1.0.0.

## 1. Цель и принцип работы

AriaSignature реализует `service-first` модель:
- локальный сервис выполняет сбор телеметрии, backup и журналирование;
- desktop UI является локальной панелью администрирования;
- API публикует состояние и результаты работы сервиса для локальной панели и для **удалённого опроса** по IP (VPN/LAN): Kestrel слушает все интерфейсы, пока в настройках `Api:Bind=all`; опциональный общий секрет для запросов не с loopback (`RemoteApiAuthMiddleware`).

Система должна работать автономно после настройки: автозапуск, плановое выполнение, доступность HTTP API.

## 2. Архитектурные границы и ответственность

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

Источники данных (в порядке приоритета):
- `smartctl` (primary);
- WMI (`Win32_DiskDrive`, `MSStorageDriver_*`);
- Storage counters (`MSFT_StorageReliabilityCounter`);
- fallback-каналы при недоступности или неполноте primary.

Правило слияния: выбираются наиболее информативные значения, идентификация выполняется по model/serial и физическим индексам, где возможно.

История срезов и SMART хранится в SQLite через `IDiskTelemetryRepository`.
Поддерживаются операции очистки SMART-истории:
- локальная очистка по выбранному диску;
- глобальная очистка по всем дискам.

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

## 5. Снимок системы и исходящая синхронизация

- `ISystemInfoService`: Windows — `WindowsSystemInfoService` (WMI `Win32_OperatingSystem`, `Win32_ComputerSystem`, `Win32_Processor`, `Win32_VideoController` + активные адреса `NetworkInterface`); иначе — `NoopSystemInfoService`.
- Ключи настроек SQLite: `OutboundSync:Enabled`, `OutboundSync:Url`, `OutboundSync:Cron` (дефолты задаются в `SqliteDatabaseInitializer`).
- Quartz: job `outbound-sync-job`, триггер `outbound-sync-trigger`, перепланирование через `IOutboundSyncCronApplier` / `QuartzOutboundSyncCronApplier`; после старта — `OutboundSyncCronSyncHostedService` подтягивает cron из БД.
- Реализация отправки: `OutboundSyncJob`, именованный `HttpClient` `AriaOutboundSync`, payload `OutboundTelemetryPayload` (JSON).

## 6. UI и host-взаимодействие

- SPA (`src/web`) отображает данные из API службы по относительному префиксу `/api/v1` (тот же хост, что и загруженная страница, обычно `127.0.0.1`) и инициирует локальные операторские действия.
- WPF host реализует:
  - запуск/проверку службы;
  - системный трей;
  - автозапуск;
  - файловые/папочные диалоги;
  - управление сервисом через postMessage bridge.

Ссылки из WebView открываются во внешнем браузере по умолчанию.
UI выходит из fallback-экрана только после подтверждения готовности API-контуров (`/api/v1/status` и `/`).
Startup-пайплайн оптимизирован под быстрый отклик: API поднимается в ранней фазе, а длительная SQLite-инициализация выполняется в background-фазе с таймаутом и telemetry-логированием.

## 7. Версионирование (обязательная синхронизация)

При изменении версии обновлять одним коммитом:
- `Directory.Build.props`;
- `installer/inno/AriaSignature.iss` (`MyAppVersion`);
- `src/web/package.json` и lockfile;
- `src/web/src/App.tsx` (константа `UI_BUILD_VERSION`);
- `docs/API.md`, `docs/USER_GUIDE.md` и при необходимости прочие документы с номером версии в шапке (`INSTALLER.md`, `RELEASE_GATE.md`, `INSTRUCTIONS.md`).

Единственный источник графики брендинга: **`assets/branding/logo.png`**. При `npm run build` создаётся **`assets/branding/icon.ico`** (exe, трей, Inno Setup). Подключение: `src/ui/AriaSignature.UI/AriaSignature.UI.csproj`, `installer/inno/AriaSignature.iss` (`SetupIconFile`).

Изменения релиза фиксировать в `docs/DEVELOPMENT_NOTES.md`.

## 8. Стандарты документации

Обязательные требования:
- документация обновляется вместе с изменением контракта/поведения;
- описывается фактическое состояние системы, без плановых формулировок;
- используется единый технический стиль: кратко, предметно, проверяемо.

Минимальный набор при каждом изменении:
- пользовательское воздействие (`USER_GUIDE.md`);
- API-контракт (`API.md`);
- техническая реализация/процесс (`DEVELOPER_GUIDE.md`, при необходимости `INSTALLER.md`, `RELEASE_GATE.md`);
- запись в `DEVELOPMENT_NOTES.md` и `CHANGELOG.md`.

## 9. Сборка и проверка

Основной gate:
- `.\scripts\release-gate.ps1`

Release-gate выполняет:
- сборку SPA;
- build/test .NET решения;
- publish UI/service;
- проверку runtime-зависимостей (`WebView2`, `smartctl`, `drivedb.h`);
- сборку Inno Setup installer.

Артефакт: `artifacts/installer/AriaSignature-Setup.exe`.
