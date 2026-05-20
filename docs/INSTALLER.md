# AriaSignature — руководство по installer

Версия документа: 1.1.1

## 1. Назначение

Документ описывает выпуск инсталлятора, который:
- устанавливает UI и Windows service;
- регистрирует и запускает `AriaSignatureService`;
- обеспечивает автозапуск службы после перезагрузки;
- удаляет компоненты корректно на uninstall.

## 2. Предварительные требования

- Windows 10/11 x64;
- .NET 8 SDK (для подготовки publish-артефактов);
- Inno Setup 6 (`ISCC.exe`);
- WebView2 offline runtime installer: `installer/webview2/MicrosoftEdgeWebView2RuntimeInstallerX64.exe`.
- `smartctl.exe` (smartmontools) доступен на build-машине в `PATH` или через `ARIASIGNATURE_SMARTCTL` (release-gate добавляет бинарник в инсталлятор автоматически).
- `drivedb.h` обязателен для корректной базы сигнатур SMART (проверяется release-gate).

## 3. Подготовка артефактов

Из корня репозитория:

```powershell
dotnet publish .\src\ui\AriaSignature.UI\AriaSignature.UI.csproj -c Release -r win-x64 --self-contained true -o .\publish\ui
dotnet publish .\src\service\AriaSignature.Service\AriaSignature.Service.csproj -c Release -r win-x64 --self-contained true -o .\publish\service
dotnet publish .\src\service\AriaSignature.MelezhHost\AriaSignature.MelezhHost.csproj -c Release -r win-x64 --self-contained true -o .\publish\melezh-host
```

## 4. Сборка installer

Стандартный способ:

```powershell
.\scripts\release-gate.ps1 -Configuration Release
```

Результат:
- `artifacts/installer/AriaSignature-Setup.exe`

Ручной способ:
- открыть `installer/inno/AriaSignature.iss` в Inno Setup;
- выполнить compile.

## 5. Требования к версии

`MyAppVersion` в `AriaSignature.iss` должен совпадать с:
- `Directory.Build.props`;
- `src/web/package.json`;
- версиями в документации.

## 6. Поведение установки/обновления

- `PrivilegesRequired=admin`, архитектура `x64compatible`;
- фильтр закрытия приложений ограничен только процессами AriaSignature (`UI/Service/API`);
- перед копированием файлов выполняется stop/delete предыдущей службы и best-effort завершение процессов `AriaSignature.*`;
- регистрация выполняется через корректно экранированный путь (`AddQuotes`);
- после регистрации выполняется проверка существования службы;
- применяется политика автозапуска и recovery;
- запуск службы после установки выполняется с retry;
- при неуспешном старте setup выполняет preflight/postflight проверки и запускает auto-repair через `AriaSignature.ServiceBootstrap.exe`;
- установка не оставляет полу-рабочее состояние: если после auto-repair не подтверждены `service running + /api/v1/status`, setup завершается ошибкой.

При критической ошибке регистрации/здоровья (`install-health:fail-hard`) установка завершается с ошибкой. Статусы `install-health:degraded-*` и `install-health:timeout` **не** прерывают мастер — пользователь может нажать «Завершить»; см. `SuppressibleMsgBox` в post-install.

Задача автозапуска трея при входе (`schtasks`, `AriaSignatureTrayLogon`) регистрируется с учётной записью `{%USERDOMAIN%}\{%USERNAME%}` (не `{userdomain}` — такой константы в Inno Setup нет).

## 7. Поведение удаления

- в `usUninstall` / `usPostUninstall` — `taskkill` для UI/Service/API/MelezhHost/oscript (Inno Setup не имеет отдельной директивы `UninstallCloseApplications`);
- best-effort stop/delete `AriaSignatureService` и `AriaSignatureMelezhService`;
- ожидание исчезновения обеих записей в SCM (`WaitServiceAbsent`);
- завершение процессов `AriaSignature.*`, `AriaSignature.MelezhHost.exe`, `oscript.exe` (bundle);
- удаление `%ProgramData%\AriaSignature\melezh\` (проект Melezh);
- в `usPostUninstall` — `RemoveAppDirectoryBestEffort`: до 3 попыток `DelTree({app})` с повторным `taskkill`;
- при неудаче — сообщение с путём `{app}` и подсказкой `scripts\repair-upgrade.ps1`.

## 7.1. Melezh при установке (1.1.0+)

- Preflight: `{app}\melezh\bin\melezh.bat`, `{app}\melezh-host\AriaSignature.MelezhHost.exe`.
- Регистрация и запуск `AriaSignatureMelezhService` — **обязательны** (fail-hard при ошибке `sc create` / `sc start`).
- Проверка `http://127.0.0.1:7788/ui` после `sc start` — при таймауте установка завершается в режиме `install-health:degraded-melezh-ui` (мастер не блокируется).
- Проверка версии API `/api/v1/status` == `MyAppVersion` (ловит «залипший» upgrade 1.0.1).
- `[Files]` для `service`, `melezh-host`, `melezh`: флаг `restartreplace` при upgrade.
- Полевое восстановление: `scripts/repair-upgrade.ps1` (служба + версия API), `scripts/repair-melezh.ps1` (только Melezh) или кнопка **«Восстановить службу Melezh»** в «Настройки».
- Если installer сообщает, что **файлы на диске** или **API** не совпадают с версией — закройте процессы, перезагрузите ПК (для отложенного `restartreplace`) и повторите setup; либо `.\scripts\repair-upgrade.ps1` от администратора.

## 8. Автозапуск UI и трей

- optional startup shortcut в `commonstartup`;
- запуск UI с параметром `--tray`;
- штатный выход выполняется через меню трея.
- флажок **Автозапуск** в настройках включает панель и тип запуска **Автоматически** для `AriaSignatureService` и `AriaSignatureMelezhService` (может запросить UAC).

## 7.2 CLI-зеркало ISS (только для агентов / CI)

**Пользовательский установщик** — только `AriaSignature-Setup.exe` (Inno Setup, [`AriaSignature.iss`](../installer/inno/AriaSignature.iss)). Логика SCM/Melezh выполняется **в Pascal**, как раньше.

**CLI** ([`scripts/install-cli.ps1`](../scripts/install-cli.ps1)) — зеркало тех же шагов для нейросетей и `release-gate`: те же `install-health:*` маркеры, fail-hard на версии API и Melezh, degraded-пути для основной службы. Не включается в состав setup.exe.

```powershell
.\scripts\install-cli.ps1 -Action Install -InstallRoot "C:\Program Files\AriaSignature" -SkipFirewall
```

Действия: `Install`, `Upgrade`, `Uninstall`, `Verify`, `Diagnose`. Реализация: [`AriaSignature.Install.IssMirror.ps1`](../installer/lib/AriaSignature.Install.IssMirror.ps1).

`release-gate` прогоняет cli-install-smoke **до** ISCC, чтобы поймать те же ошибки SCM, что и в ISS, без замены production-installer.

## 7.3 Bundle OInt / Melezh

Манифест обязательных путей: [`installer/melezh/required-files.json`](../installer/melezh/required-files.json). Проверка: `.\scripts\Test-MelezhBundle.ps1 -RootPath .\installer\melezh\bundle`.

## 9. Верификация после установки

Минимальные проверки:
- служба `AriaSignatureService` существует и запущена;
- служба `AriaSignatureMelezhService` запущена, `http://127.0.0.1:7788/ui` отвечает;
- `/api/v1/status` → `version` совпадает с версией installer;
- в «Настройки» виден блок **Melezh / OpenIntegrations**;
- UI открывается без ошибки WebView2;
- API доступен на `http://127.0.0.1:{port}/api/v1/status`;
- при необходимости с другой машины в VPN: `http://<IP_агента>:{port}/api/v1/status` (и с заголовком авторизации, если задан токен в настройках);
- low-level SMART канал активен (в логах сервиса есть строка про `smartctl` либо метрики дисков показывают температуру/health, когда поддерживается устройством);
- в `{app}\service\smartctl` присутствуют `smartctl.exe` и `drivedb.h` с ненулевым размером;
- создание и запуск backup-задачи выполняются успешно;
- журнал содержит запись о выполнении.

## 10. Брандмауэр и порт API

- При установке выполняется команда `netsh`, добавляющая входящее правило **«AriaSignature API (TCP 5160)»** для TCP-порта **5160**.
- Если порт API изменён в настройках службы, обновите правило вручную в «Брандмауэр Windows в режиме повышенной безопасности» или удалите старое и создайте новое для актуального порта.
- При удалении продукта установщик выполняет best-effort удаление правила с тем же именем.

## 11. Инцидентные заметки

- SCM/install/uninstall + Melezh autostart incident: [`docs/INCIDENT_SCM_INSTALL_UNINSTALL_MELEZH.md`](./INCIDENT_SCM_INSTALL_UNINSTALL_MELEZH.md)
