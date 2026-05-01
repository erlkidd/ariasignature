# Release Gate (RC) AriaSignature

Перед выпуском нового `installer` каждый пункт должен быть выполнен успешно.

## Автоматические шаги (скрипт)

Скрипт `scripts/release-gate.ps1` выполняет:

1. `npm ci` и `npm run build` в `src/web` (сборка SPA в `wwwroot` службы)
2. `dotnet build .\AriaSignature.slnx -c Release`
3. `dotnet test .\AriaSignature.slnx -c Release`
4. `dotnet publish` UI и Service в `publish\ui` и `publish\service`
5. `iscc .\installer\inno\AriaSignature.iss`

Единый запуск:

- `powershell -ExecutionPolicy Bypass -File .\scripts\release-gate.ps1`

Требования на машине сборки: **Node.js LTS**, **.NET 8 SDK**, **Inno Setup 6**.

## Ручной приемочный чеклист

- Установщик полностью русскоязычный и запрашивает права администратора.
- После установки служба `AriaSignatureService` существует и запущена (имя без пробела, как в коде установщика).
- На ПК пользователя установлен **WebView2 Runtime**; окно панели открывается без ошибки инициализации WebView2.
- Панель загружает интерфейс с `http://127.0.0.1:{порт}/` (по умолчанию 5160); диски и журнал отображаются.
- Закрытие окна сворачивает приложение в трей, пункт «Выход» завершает приложение.
- Архивация: файловая и MSSQL, проверка подключения MSSQL, расписание и журнал с фильтром.
- Удаление приложения корректно останавливает и удаляет службу.
