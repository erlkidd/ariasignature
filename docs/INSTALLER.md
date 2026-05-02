# AriaSignature — руководство по installer

Версия документа: 0.9.9.

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
dotnet publish .\src\ui\AriaSignature.UI\AriaSignature.UI.csproj -c Release -o .\publish\ui
dotnet publish .\src\service\AriaSignature.Service\AriaSignature.Service.csproj -c Release -o .\publish\service
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
- если служба не стартовала в окне установки, инсталляция завершается успешно с информационным уведомлением и дальнейшим восстановлением через UI.

При критической ошибке регистрации (невозможно создать службу) установка завершается с ошибкой.

## 7. Поведение удаления

- best-effort stop/delete `AriaSignatureService`;
- допустимые состояния: служба отсутствует / уже остановлена;
- дополнительно выполняется завершение процессов `AriaSignature.*`;
- удаляется каталог установки целиком (`{app}`), чтобы reinstall не наследовал старые бинарники.

## 8. Автозапуск UI и трей

- optional startup shortcut в `commonstartup`;
- запуск UI с параметром `--tray`;
- штатный выход выполняется через меню трея.

## 9. Верификация после установки

Минимальные проверки:
- служба `AriaSignatureService` существует и запущена;
- UI открывается без ошибки WebView2;
- API доступен на `http://127.0.0.1:{port}/api/v1/status`;
- low-level SMART канал активен (в логах сервиса есть строка про `smartctl` либо метрики дисков показывают температуру/health, когда поддерживается устройством);
- в `{app}\service\smartctl` присутствуют `smartctl.exe` и `drivedb.h` с ненулевым размером;
- создание и запуск backup-задачи выполняются успешно;
- журнал содержит запись о выполнении.
