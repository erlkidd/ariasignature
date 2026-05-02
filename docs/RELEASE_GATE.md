# AriaSignature — регламент release-gate

Версия документа: 0.9.6.

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
4. `dotnet publish` UI и Service
5. подготовку runtime-зависимостей (`WebView2`, `smartctl`, `drivedb.h`) с проверкой целостности
6. `ISCC` сборку `installer/inno/AriaSignature.iss`

Выходной артефакт:
- `artifacts/installer/AriaSignature-Setup.exe`

## 3. Требования к build-агенту

- Node.js LTS;
- .NET 8 SDK;
- Inno Setup 6;
- доступ к интернету для загрузки WebView2 offline runtime installer (если отсутствует локально).

## 4. Ручной чеклист приемки

После установки инсталлятора:

- служба `AriaSignatureService` зарегистрирована и в состоянии `Running`;
- UI запускается без ошибок и доступен в трее;
- API отвечает на `GET /api/v1/status`;
- в `{app}\service\smartctl` присутствуют `smartctl.exe` и `drivedb.h`, размер файлов > 0;
- `POST /api/v1/disks/refresh` возвращает срез;
- создание и запуск backup-задачи проходят штатно;
- записи появляются в `/api/v1/backups/logs`;
- после перезагрузки ОС служба запускается автоматически;
- uninstall корректно удаляет службу и компоненты.

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
gh release create v0.9.6 --title "AriaSignature 0.9.6" --generate-notes artifacts/installer/AriaSignature-Setup.exe
```

Замените номер версии и при необходимости добавьте `--notes "…"` или файл с кратким описанием вместо `--generate-notes`. Полный журнал см. в `docs/CHANGELOG.md`.

Либо создайте релиз вручную на странице **Releases** репозитория и загрузите `.exe` как binary attachment.
