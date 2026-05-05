# Инцидент: регрессия запуска на Win10/Win11

## Цель документа

Закрыть инцидент запуска окончательно: служба, API, UI и установщик должны стабильно стартовать на Windows 10 и Windows 11 без циклов `SC 1053`, `SC 1072`, падений UI и «тихого» нестарта.

## Как это выглядело для пользователя

- Установщик мог зависать на этапе регистрации/старта службы.
- `sc start` возвращал `1053`, после чего процесс службы отсутствовал.
- При переустановке всплывал `sc create` с кодом `1072` (служба «помечена на удаление»).
- UI иногда «не открывался» (silent-exit/трей/single-instance), либо падал сразу после запуска.
- В ряде сценариев мастер установки вел себя «неровно» (долгое ожидание, системный beep, неудобная UX-ветка при ошибках).

## Корневые причины (подтвержденные)

### 1) Падение службы на раннем bootstrap (`PlatformNotSupportedException`)

Симптом из логов `%ProgramData%\AriaSignature\logs\service-startup-fatal.log`:

- `marker=fatal stage=startup-bootstrap`
- `System.PlatformNotSupportedException` в `HostApplicationBuilder.Build()` / `WindowsServiceLifetime`.

Причина: несогласованная связка publish/hosting для Windows Service (пакет `Microsoft.Extensions.Hosting.WindowsServices` не в линии net8 + артефакт, попадающий в installer, не всегда соответствовал ожидаемой конфигурации).

### 2) Гонка SCM при удалении/создании службы (`SC 1072`)

`sc delete` асинхронен; служба может оставаться в pending-delete.
Старая логика ожидания в installer ориентировалась только на код `1060`, из-за чего `sc create` выполнялся слишком рано и падал с `1072`.

### 3) Регрессия UI после внедрения bootstrap-помощника

Критичная находка из практики: bootstrap-пакет копировался в корень `publish/ui` и подменял WPF-зависимости UI (`WindowsBase`), что приводило к падению UI при старте (`.NET Runtime 1026`, `FileNotFoundException: WindowsBase, Version=8.0.0.0`).

### 4) Переизбыточный fail-hard в installer/UI startup

Часть recoverable-сценариев обрабатывалась как фатальные слишком рано: пользователь получал «поломанный» UX вместо детерминированного recovery с понятной диагностикой.

## Принятая стратегия исправления

Откат истории/веток не используется. Исправления — только in-place, с усилением всех этапов старта.

### A. Служба: выравнивание hosting-пути и фатальная диагностика

Файлы:
- `src/service/AriaSignature.Service/AriaSignature.Service.csproj`
- `src/service/AriaSignature.Service/Program.cs`
- `src/service/AriaSignature.Service/LocalApiHostedService.cs`

Сделано:
- `TargetFramework` и зависимость `Microsoft.Extensions.Hosting.WindowsServices` приведены к линии `net8.0-windows` (`8.x`).
- Добавлены/усилены фатальные маркеры запуска в `service-startup-fatal.log`.
- Локальный API запускается в неблокирующем режиме для SCM.

### B. Release gate: контроль publish-артефактов и версии hosting

Файл: `scripts/release-gate.ps1`

Сделано:
- Явная проверка, что service publish содержит `WindowsServices` версии `8.x`.
- Очистка `publish/ui` и `publish/service` перед publish, чтобы исключить «мусор» прошлых сборок.
- Проверки наличия ключевых self-contained артефактов.

### C. Bootstrap: изоляция от UI runtime

Файлы:
- `scripts/release-gate.ps1`
- `src/ui/AriaSignature.UI/MainWindow.xaml.cs`
- `src/ui/AriaSignature.UI/AriaSignature.UI.csproj`
- `src/ui/AriaSignature.UI/Services/WindowsServiceEnsure.cs`
- `installer/inno/AriaSignature.iss`

Сделано:
- `AriaSignature.ServiceBootstrap.exe` и его payload перенесены в подпапку `ui/bootstrap`.
- UI и installer обновлены на новый путь helper.
- Исключено подмешивание bootstrap runtime в корень UI publish.

### D. Installer: устойчивое ожидание SCM и retry под `1072`

Файл: `installer/inno/AriaSignature.iss`

Сделано:
- `WaitServiceAbsent` анализирует полный вывод `sc query`, включая `DELETE_PENDING`/`MARKED FOR DELETE`.
- Увеличено ожидание и усилены retries `sc create` с backoff для `SC_MARKED_FOR_DELETE`.
- Preflight-проверки обязательных бинарников и детерминированные диагностические сообщения.

### E. UI: видимость запуска и anti-silent-exit

Файлы:
- `src/ui/AriaSignature.UI/App.xaml.cs`
- `src/ui/AriaSignature.UI/SingleInstanceActivator.cs`

Сделано:
- При втором экземпляре больше нет «тихого» закрытия без объяснения.
- Улучшена активация существующего окна и ветка stale-mutex.

## Обязательный протокол проверки (release gate)

Для каждого RC/релиза в строгом порядке:

1. `dotnet build AriaSignature.slnx -c Release`
2. `dotnet test AriaSignature.slnx -c Release`
3. `powershell -ExecutionPolicy Bypass -File .\scripts\release-gate.ps1`
4. Чистый install smoke на Win10 и Win11:
   - установщик завершился;
   - `AriaSignatureService` зарегистрирована и запущена;
   - `http://127.0.0.1:5160/api/v1/status` возвращает `200`;
   - UI открывается из setup и с ярлыка.
5. При сбое собираются:
   - `%ProgramData%\AriaSignature\logs\bootstrap-last-result.txt`
   - последний `%ProgramData%\AriaSignature\logs\service-*.log`
   - `%ProgramData%\AriaSignature\logs\service-startup-fatal.log`
   - точный текст окна installer/UI.

Без этой матрицы релиз не принимается.

## Защита от повторения инцидента

- Не добавлять рискованные OS/security вызовы в pre-run службы без non-fatal обертки.
- Не смешивать runtime payload разных приложений в одном publish-каталоге.
- Не превращать recoverable startup-ветки в преждевременный fail-hard.
- Любой сбой запуска должен иметь:
  - детерминированный лог-файл;
  - явный user-facing hint;
  - проверяемый stage marker.

