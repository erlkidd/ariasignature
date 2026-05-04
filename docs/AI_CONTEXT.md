# AriaSignature - AI Context

Версия документа: 1.0.1.

## 1) Что это за система

- `AriaSignature.Service` - главный runtime процесса (Windows Service).
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

1. Запуск `AriaSignature.Service`.
2. Инициализация хоста и логирования.
3. Запуск `LocalApiHostedService` и bind URL.
4. Готовность API проверяется локальным probe (`/api/v1/status`).
5. UI подключается к `127.0.0.1:{port}` и отображает SPA.

## 4) Диагностика проблем

- Логи службы: `%ProgramData%/AriaSignature/logs/service-*.log`.
- Если сервис не стартует после установки:
  - проверить `services.msc` (`AriaSignatureService`);
  - проверить порт `5160` (конфликт);
  - проверить `Api:Bind` (`all`/`loopback`) в настройках.
- Инсталлятор логирует шаги `sc create/start` и health probe API.

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
