# AriaSignature — operations runbook

Версия документа: 1.0.1.

## 1. Цель

Дать команде единый алгоритм triage/recovery при сбоях installer, service startup, UI startup и runtime деградациях.

## 2. Базовые SLI/SLO (для небольшой команды)

- **SLI: API readiness latency** — время до `GET /api/v1/status = 200` после старта службы.
  - **SLO (p95):** <= 120s.
- **SLI: backup success ratio** — доля успешных backup run.
  - **SLO (rolling 24h):** >= 0.90.
- **SLI: smart refresh success ratio** — доля успешных smart refresh run.
  - **SLO (rolling 24h):** >= 0.90.
- **SLI: outbound sync success ratio** (если включен).
  - **SLO (rolling 24h):** >= 0.85.
- **SLI: db busy retries** — рост счетчика retry в SQLite миграциях/операциях.
  - **SLO:** без длительного непрерывного роста.

## 3. Источники диагностики

- `%ProgramData%\AriaSignature\logs\service-startup-fatal.log`
- `%ProgramData%\AriaSignature\logs\service-*.log`
- `%ProgramData%\AriaSignature\logs\bootstrap-last-result.txt`
- API:
  - `GET /api/v1/health/live`
  - `GET /api/v1/health/ready`
  - `GET /api/v1/health/degradation`
  - `GET /api/v1/observability/runtime`

## 4. Triage playbook

1. Проверить доступность API:
   - `GET /api/v1/health/live`
   - `GET /api/v1/health/ready`
2. Проверить деградацию:
   - `GET /api/v1/health/degradation`
   - если `status=degraded`, фиксировать `reasons`.
3. Снять runtime baseline:
   - `GET /api/v1/observability/runtime`
   - зафиксировать latency/ratio/retries.
4. Проверить startup классификацию:
   - installer/status маркеры `install-health:*`
   - UI warning markers: `reliability-state=degraded`, `setup-category=*`.
5. Если service startup сбой:
   - проверить `service-startup-fatal.log` и `bootstrap-last-result.txt`.
6. Если API недоступен при RUNNING service:
   - сопоставить с `api-started`, `api-probe`, readiness endpoint и порт/Bind-конфигурацией.

## 5. Alarm policy (минимум)

- **Critical:**
  - `health/live` недоступен;
  - repeated `install-health:degraded-service-not-running`;
  - `setup-category=ServiceCrashedOnStart`.
- **Warning:**
  - `health/degradation` -> `api-startup-latency-high`;
  - backup/smart/outbound success ratio ниже SLO;
  - быстрый рост `db-busy-retries`.

## 6. Recovery patterns

- installer/service registration проблемы:
  - переустановка из актуального installer;
  - проверка SCM (`services.msc`) и UAC/elevation;
  - проверка блокировок безопасности/антивируса.
- API warmup проблемы:
  - сверка `Api:Port`, `Api:Bind`, локального firewall;
  - перезапуск службы после подтверждения конфигурации.
- runtime деградация backup/smart:
  - проверить доступ к источнику/назначению backup;
  - проверить доступность smartctl/WMI контуров.

## 7. Post-incident loop (обязательно)

После каждого инцидента:

1. Классифицировать тип (`installer`, `startup`, `runtime`, `release-regression`).
2. Добавить preventive change:
   - тест,
   - gate-check,
   - логический guardrail.
3. Обновить:
   - `docs/INCIDENT_STARTUP_W10_W11.md`,
   - `docs/RELEASE_GATE.md`,
   - `docs/CHANGELOG.md`.
