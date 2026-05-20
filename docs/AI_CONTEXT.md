# AriaSignature - AI Context

Версия документа: 1.1.1

## 1) Что это за система

- `AriaSignature.Service` - главный runtime процесса (Windows Service), API порт **5160**.
- `AriaSignature.MelezhHost` + `AriaSignatureMelezhService` - Melezh (OpenIntegrations), порт **7788**.
- `AriaSignature.Api` - REST API `/api/v1` (Kestrel), Swagger `/swagger`.
- `AriaSignature.UI` - desktop shell (WPF + WebView2).
- `src/web` - React SPA, загружается из `service/wwwroot`.
- `AriaSignature.Infrastructure` - SQLite, SMART/WMI, backup execution.

Ключевой принцип: данные и фоновые задачи живут в службе; UI выступает как операторская панель.

## 2) Инварианты

- Служба должна запускаться без UI.
- API `/api/v1/status` должен отвечать при здоровом старте службы.
- UI и удалённые клиенты читают одни и те же данные через API.
- Изменения поведения должны отражаться в `docs/CHANGELOG.md`.

## 3) Поток старта

**Права и цепочка (Win10/11):** установщик **Inno** (`PrivilegesRequired=admin`) регистрирует службу и задаёт ожидаемый путь `{app}\service\AriaSignature.Service.exe` — тот же относительный путь, что UI строит как `..\service\` от `{app}\ui`. Обычный пользователь может получить **отказ в доступе** при `sc`/SCM; тогда UI **один раз за процесс** запускает рядом лежащий **`AriaSignature.ServiceBootstrap.exe`** через **UAC** (`runas`), helper повторяет ту же процедуру `create/start`, логируя шаги в `%ProgramData%\AriaSignature\logs\bootstrap-*.log`.
Артефакты UI/Service публикуются как `win-x64` self-contained: запуск не зависит от внешней установки .NET runtime на целевой Win11.
При **SCM 1053** выполняется расширенная post-1053 проверка: timeline статусов службы (до ~120 с), наличие процесса хоста и probe API; только после этого фиксируется финальный фейл с stage-маркером (`scm-timeout-1053` / `post-1053-check`).

1. Запуск `AriaSignature.Service`.
2. Инициализация хоста и логирования.
3. Запуск `LocalApiHostedService` как `BackgroundService`: SCM получает Running без блокировки на долгом подъёме Kestrel; затем bind URL и слушание порта.
4. Готовность API проверяется локальным probe (`/api/v1/status`).
5. UI подключается к `127.0.0.1:{port}` и отображает SPA.

## 4) Диагностика проблем

- Логи службы: `%ProgramData%/AriaSignature/logs/service-*.log`.
- Если сервис не стартует после установки:
  - проверить `services.msc` (`AriaSignatureService`);
  - код **1053** у SCM — часто таймаут ответа при долгом старте; проверить состояние службы и логи через минуту, см. `docs/USER_GUIDE.md` §9;
  - проверить порт `5160` (конфликт);
  - проверить `Api:Bind` (`all`/`loopback`) в настройках.
- Инсталлятор логирует шаги `sc create/start`, а при неуспехе старта дополнительно пишет полный `sc query` и `sc qc`; bootstrap пишет timeline/checkpoint-маркеры для 1053-диагностики.
- Setup использует статус `install-health` (`ok`, `fail-with-repair`, `fail-hard`) и после неуспешного старта выполняет auto-repair; `fail-hard` блокирует завершение установки в полу-рабочем состоянии.

## 5) Карта тестов

- `tests/AriaSignature.ApiTests` - контракт и smoke API.
- `tests/AriaSignature.UnitTests` - парсинг телеметрии и внутренние edge-case проверки.

## 6) Частые ловушки

- На части хостов Win11 dual-stack bind (`[::]`) может ломать старт.
- `smartctl` может быть недоступен: тогда система работает через WMI/Storage fallback.
- Наработка и ресурс SSD зависят от источника и формата атрибутов.

## 7) Рекомендации для AI-агента

- Перед правками проверять границы слоёв (`Domain/Application/Infrastructure/Api/Service/UI`).
- Не менять публичный контракт API без обновления `docs/API.md`.
- Для релизных изменений обновлять `CHANGELOG.md` и синхронизировать версии.
