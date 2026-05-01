# Руководство по установщику AriaSignature

**Версия установщика** задаётся `#define MyAppVersion` в `AriaSignature.iss` и должна совпадать с `Directory.Build.props` (сейчас **0.2.1**).

## Требования

- Windows 10/11 x64.
- Установлен Inno Setup 6.
- Подготовлены publish-артефакты UI и службы.
- WebView2 bootstrapper `installer/webview2/MicrosoftEdgeWebView2Setup.exe` (release-gate загрузит автоматически, если файла нет).

## Подготовка publish-артефактов

Запускать из корня репозитория:

```powershell
dotnet publish .\src\ui\AriaSignature.UI\AriaSignature.UI.csproj -c Release -o .\publish\ui
dotnet publish .\src\service\AriaSignature.Service\AriaSignature.Service.csproj -c Release -o .\publish\service
```

## Сборка установщика

- Откройте `installer/inno/AriaSignature.iss` в Inno Setup Compiler и выполните сборку.
- Готовый файл появляется в `artifacts/installer`.
- Для полного автоматического прогона используйте `.\scripts\release-gate.ps1`.

## Права и безопасность

- Установщик требует права администратора (`PrivilegesRequired=admin`).
- Архитектура установщика: `x64compatible`.
- Служба устанавливается с автостартом и политикой восстановления.
- Деинсталлятор корректно останавливает и удаляет службу.
- Если WebView2 Runtime отсутствует, установщик запускает его тихую установку перед первым стартом UI.

## Поведение службы при установке/обновлении

- Путь к `AriaSignature.Service.exe` в `sc create` передаётся через `AddQuotes` (обязательно для `C:\Program Files\...`); иначе `sc.exe` некорректно разбирает аргументы и служба не появляется в `services.msc`.
- Для вызова используется 64-битный `sc.exe` (`{sysnative}\sc.exe` с запасными вариантами).
- После `sc create` выполняется проверка `sc query AriaSignatureService`; при отсутствии записи установка прерывается с сообщением.
- Перед регистрацией новой службы выполняется deterministic stop/delete предыдущей версии.
- Служба создается заново с автостартом и recovery policy.
- При ошибке регистрации или запуска установщик завершает процесс с явной ошибкой.

## Поведение при удалении

- На uninstall выполняется best-effort stop/delete `AriaSignatureService`.
- Состояния «службы нет» и «служба уже остановлена» считаются допустимыми.
- Данные внутри каталога установки (`{app}`), включая SQLite и логи, удаляются.

## Автозапуск и трей

- Установщик может добавить ярлык автозапуска в `commonstartup`.
- Ярлык запускает UI с параметром `--tray`.
- Закрытие окна сворачивает приложение в трей, полный выход выполняется через меню трея.

## Иконки

- Исходный ресурс: `icon.png` (корень репозитория).
- Единый `icon.ico` используется в exe, окне, трее, ярлыках и установщике.
- В скрипте включен `SetupIconFile`.
