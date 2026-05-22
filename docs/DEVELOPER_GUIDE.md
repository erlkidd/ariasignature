# AriaSignature — руководство разработчика

Версия документа: 1.1.2

## 1. Цель и принцип работы

AriaSignature реализует `service-first` модель:
- локальный сервис выполняет сбор телеметрии, backup и журналирование;
- desktop UI является локальной панелью администрирования;
- API публикует состояние и результаты работы сервиса для локальной панели и для **удалённого опроса** по IP (VPN/LAN): Kestrel слушает все интерфейсы, пока в настройках `Api:Bind=all`; опциональный токен для запросов не с loopback (`RemoteApiAuthMiddleware`).

Система должна работать автономно после настройки: автозапуск, плановое выполнение, доступность HTTP API.

### 1.1 Границы продукта

**Включено:** диагностика дисков (HDD/SSD/NVMe), архивация `.1CD` и MSSQL, HTTP API `/api/v1`, панель и трей, установщик, опционально Melezh (порт 7788) и исходящий POST на коллектор.

**Исключено:** облачная инфраструктура заказчика, маршрутизация/VPN между узлами, сторонние панели мониторинга вне API агента.

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
- `src/web/package.json` и `package-lock.json`;
- `src/web/src/App.tsx` (константа `UI_BUILD_VERSION`);
- `docs/API.md`, `docs/USER_GUIDE.md`, `docs/MELEZH.md`, `NAVIGATION-DOCS.MD` и прочие документы с номером версии в шапке (`INSTALLER.md`, `RELEASE_GATE.md`, `DEVELOPER_GUIDE.md`, `AI_CONTEXT.md`, `OPERATIONS_RUNBOOK.md`, `README.md`).

Release-gate публикует также `AriaSignature.MelezhHost` в `publish/melezh-host`; bundle Melezh — `installer/melezh/` (см. `scripts/prepare-melezh.ps1`).

Единственный источник графики брендинга: **`assets/branding/logo.png`**. При `npm run build` создаётся **`assets/branding/icon.ico`** (exe, трей, Inno Setup). Подключение: `src/ui/AriaSignature.UI/AriaSignature.UI.csproj`, `installer/inno/AriaSignature.iss` (`SetupIconFile`).

Изменения релиза фиксировать в `docs/CHANGELOG.md`. Карта документации: [`NAVIGATION-DOCS.MD`](../NAVIGATION-DOCS.MD).

## 8. Стандарты документации

При изменении эндпоинтов `/api/v1/*`: обновить [`docs/API.md`](API.md), [`docs/MELEZH_HANDLER_CATALOG.md`](MELEZH_HANDLER_CATALOG.md) при мосте Melezh, запустить `.\scripts\Test-ApiDocParity.ps1` и `.\scripts\Test-MelezhCatalogParity.ps1` (входят в `release-gate`).

Обязательные требования:
- документация обновляется вместе с изменением контракта/поведения;
- описывается фактическое состояние системы, без плановых формулировок;
- используется единый технический стиль: кратко, предметно, проверяемо.

Минимальный набор при каждом изменении:
- пользовательское воздействие (`USER_GUIDE.md`);
- API-контракт (`API.md`);
- техническая реализация/процесс (`DEVELOPER_GUIDE.md`, при необходимости `INSTALLER.md`, `RELEASE_GATE.md`);
- запись в `CHANGELOG.md`.

## 9. Сборка и проверка

Основной gate:
- `.\scripts\release-gate.ps1`

Release-gate выполняет:
- сборку SPA;
- build/test .NET решения;
- `Test-ApiDocParity.ps1`, `Test-MelezhCatalogParity.ps1`;
- smoke Melezh (bundle, CLI, handler bootstrap, aria bridge, write handlers);
- publish UI/service;
- проверку runtime-зависимостей (`WebView2`, `smartctl`, `drivedb.h`);
- сборку Inno Setup installer.

### 9.1 Матрица ручной проверки службы (Windows 10 / 11)

| Сценарий | Ожидание |
|----------|----------|
| Установка под администратором, холодный старт | Служба **Running**, probe `/api/v1/status` ок; при медленном старте возможна **1053** с последующим Running (см. логи). |
| Установка: служба не стартовала | В логе Inno — **sc query** / **sc qc**; сообщение про UAC / **ServiceBootstrap**. |
| UI под ограниченным пользователем, служба **Stopped** | Один запрос UAC → bootstrap; отмена — подсказка services.msc. |
| UI под администратором | Без лишних UAC при успешном `TryInstallAndStart`. |
| Служба **Running**, API не отвечает | Порт, брандмауэр, `service-*.log` (не путать с правами SCM). |

Артефакт: `artifacts/installer/AriaSignature-Setup.exe`.

## 10. Ветки Git

- **Основная линия релиза:** `production`. Актуальные изменения попадают в прод через merge/fast-forward с рабочих веток (`test/agent-work`, `test/remote-network-api` и т.п.), затем push на настроенные remotes.
- **Архивная ветка** `test/agent-work-legacy-pre-opt` (в т.ч. на `old-origin`): исторический снимок, **не сливается** в `production` и **не удаляется** — оставлена для справки. Все прочие неархивные ветки при выкатке приводятся к состоянию `production`.
