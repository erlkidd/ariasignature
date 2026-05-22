# AriaSignature — регламент release-gate

Версия документа: 1.1.2

## 1. Цель

Release-gate подтверждает, что релизный инсталлятор:
- собирается без ошибок;
- проходит тестовый набор;
- устанавливается и работает в целевом сценарии эксплуатации.

## 2. Автоматический pipeline

Команда:

```powershell
.\scripts\release-gate.ps1 -Configuration Release
```

Pipeline выполняет:
1. `npm ci` + `npm run build` (`src/web`)
2. `dotnet build .\AriaSignature.slnx -c Release`
3. `dotnet test .\AriaSignature.slnx -c Release`
4. `Test-ApiDocParity.ps1`, `Test-MelezhCatalogParity.ps1` (код ↔ `docs/API.md` ↔ каталог Melezh)
5. `dotnet publish` UI, Service и **MelezhHost** в `win-x64 --self-contained true`; bootstrap публикуется отдельным шагом в `publish/bootstrap` и затем копируется в `publish/ui` (единственный разрешённый источник bootstrap для installer)
6. подготовку runtime-зависимостей (`WebView2`, `smartctl`, `drivedb.h`, **Melezh/oint** bundle) с проверкой целостности и host-dependency-check (`powershell/sc/taskkill`, запись в `%ProgramData%\AriaSignature\logs\`)
   - `WebView2` принимается только как standalone offline installer: проверяется минимальный размер файла и валидная Microsoft-подпись (bootstrap online-пакет блокирует gate)
   - `prepare-melezh.ps1`: извлекает OInt/Melezh из `installer/melezh/oint_*_installer_ru.exe` в `installer/melezh/bundle/` (обязательно перед Inno), см. `installer/melezh/README.md`
7. regression smoke для уже установленной службы (`scripts/service-startup-smoke.ps1`) — если служба присутствует на build-host
8. `Test-MelezhBundle.ps1` — полнота OInt bundle;
8. `install-cli.ps1` smoke (install → verify → uninstall во временную папку);
10. `ISCC` сборку `installer/inno/AriaSignature.iss`

Опционально (локально): `Invoke-Pester .\tests\installer\ -ExcludeTag Integration`

Выходной артефакт:
- `artifacts/installer/AriaSignature-Setup.exe`

## 3. Требования к build-агенту

- Node.js LTS;
- .NET 8 SDK;
- Inno Setup 6;
- доступ к интернету для загрузки WebView2 offline runtime installer (если отсутствует локально).

## 4. Ручной чеклист приемки

После установки инсталлятора:

- службы `AriaSignatureService` и `AriaSignatureMelezhService` зарегистрированы и в состоянии `Running` (Melezh — при наличии bundle);
- UI запускается без ошибок и доступен в трее (после reboot — автозапуск Startup + `AriaSignatureTrayLogon`);
- `http://127.0.0.1:7788/ui` открывается при работающем Melezh;
- API отвечает на `GET /api/v1/status` (локально и, при сценарии с VPN, опционально с другой машины по `http://<IP_агента>:5160/api/v1/status`);
- в `{app}\service\smartctl` присутствуют `smartctl.exe` и `drivedb.h`, размер файлов > 0;
- `POST /api/v1/disks/refresh` возвращает срез;
- создание и запуск backup-задачи проходят штатно;
- записи появляются в `/api/v1/backups/logs`;
- после перезагрузки ОС служба запускается автоматически; панель UI появляется в трее (ярлык в автозагрузке пользователя и/или задача планировщика);
- uninstall корректно удаляет службу и компоненты.

### UX критерии мастера (обязательные)

- во время post-install окно мастера остаётся отзывчивым: его можно перемещать и сворачивать;
- в статусной строке нет длительного «пустого» состояния на старте установки;
- при долгом старте службы/API используется hybrid-режим: ограниченное ожидание (таймаут) и завершение установки без бесконечной блокировки;
- кнопка `Завершить` доступна после успешного копирования файлов, даже если служба/API продолжает прогрев в фоне;
- при срабатывании таймаута в логе есть маркер `install-health:timeout`, а пользователю показывается путь к `%ProgramData%\AriaSignature\logs\`.

### Observability критерии (обязательные)

- API должен отвечать на `GET /api/v1/health/live`, `GET /api/v1/health/ready`, `GET /api/v1/health/degradation`;
- API должен отвечать на `GET /api/v1/observability/runtime` с метриками startup/backup/smart/outbound/db-retry;
- на каждый API-ответ возвращается `X-Correlation-Id` (или эхо клиентского заголовка, или сгенерированный сервером);
- для triage используется единый runbook: `docs/OPERATIONS_RUNBOOK.md`.

### Матрица Win11 (обязательная)

- `fresh install` (стандартный пользователь): setup проходит preflight, при сбое старта выполняется auto-repair.
- `fresh install` (администратор): подтверждается `install-health:ok`.
- сценарий `1053`: post-1053-check фиксирует timeline и исход (`Running` или детализированный `fail-hard`).
- сценарий падения службы: setup и UI показывают stage-код и путь к логам.
- сценарий блокировки (антивирус/Controlled Folder Access): получаем детерминированный `fail-hard` без «тихого успеха».
- reinstall поверх старой версии: удаление/повторная регистрация службы проходит штатно.

## 5. Критерии блокировки релиза

Релиз запрещен при любом из условий:
- падение gate-скрипта;
- несоответствие версий между артефактами;
- неуспешные smoke-check сценарии после установки;
- деградация API-контракта без обновления документации.

## 6. Публикация на GitHub Releases

После успешного gate и коммита с обновлённой версией:

1. Создать аннотированный тег `v{версия}` и отправить ветку и теги на GitHub.
2. Опубликовать релиз с прикреплённым файлом `artifacts/installer/AriaSignature-Setup.exe`, например через GitHub CLI:

```powershell
gh release create v1.0.0 --title "AriaSignature 1.0.0" --generate-notes artifacts/installer/AriaSignature-Setup.exe
```

Замените номер версии и при необходимости добавьте `--notes "…"` или файл с кратким описанием вместо `--generate-notes`. Полный журнал см. в `docs/CHANGELOG.md`.

Либо создайте релиз вручную на странице **Releases** репозитория и загрузите `.exe` как binary attachment.
