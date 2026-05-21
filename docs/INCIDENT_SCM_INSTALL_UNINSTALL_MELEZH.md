# Incident: SCM install/uninstall + Melezh autostart

Версия документа: 1.1.1

## Симптомы

- На установке:
  - `AriaSignatureService still present in SCM before create`.
  - `Служба AriaSignatureService не удалена из Windows (SCM) перед регистрацией новой версии`.
  - `Runtime error ... Unknown constant "userdomain"` — неверная константа Inno в регистрации tray-задачи.
  - экран «установка завершена», но кнопка **Завершить** не закрывает мастер (исключение в `ssPostInstall`).
- На удалении:
  - uninstall завершается, но службы остаются в `services.msc`.
  - папка `C:\Program Files\AriaSignature` (или выбранный `{app}`) остаётся на диске.
- По Melezh:
  - после запуска AriaSignature `http://127.0.0.1:7788/ui` недоступен;
  - `GET :7788/aria_ping` / `aria_get_*` → `result:false`, «Некорректное имя команды: http» или «Ошибка в названии команды или функции обработчика»;
  - кнопка **Восстановить службу Melezh** в UI чинит проблему.

## Корневая причина

1. Race condition в SCM: служба иногда еще присутствует в базе SCM (или `marked for delete`) в момент pre-create проверки.
2. Преждевременный fail-hard до полного контура retry `sc create`.
3. На uninstall не хватало расширенного second-pass удаления.
4. UI startup/recovery гарантировал подъем только `AriaSignatureService`, а не `AriaSignatureMelezhService`.
5. В ISS использовалась несуществующая константа `{userdomain}` вместо `{%USERDOMAIN%}` / `{%USERNAME%}`.
6. Проверка `InstallHealthStatus <> 'install-health:ok'` прерывала установку при любом `degraded-*`, хотя ниже были сообщения про «ограниченную готовность» (с 1.1.1+ degraded только в лог, без MsgBox).
7. В OInt 0.12 bundle отсутствовал CLI-индекс модуля `http` → handlers с `library=http` не вызывались; bootstrap v1 пропускал существующие keys и не чинил после upgrade.
8. Удаление `{app}` полагалось только на `[UninstallDelete]` без `DelTree` после остановки процессов в `usPostUninstall`.

## Быстрый triage

```powershell
sc query AriaSignatureService
sc queryex AriaSignatureService
sc query AriaSignatureMelezhService
sc qc AriaSignatureMelezhService
```

```powershell
Invoke-WebRequest http://127.0.0.1:5160/api/v1/status -UseBasicParsing
Invoke-WebRequest http://127.0.0.1:7788/ui -UseBasicParsing
```

```powershell
# admin
.\scripts\diagnose-melezh.ps1
```

Логи:

- `%ProgramData%\AriaSignature\logs\service-*.log`
- `%ProgramData%\AriaSignature\logs\melezh-host-*.log`
- `%ProgramData%\AriaSignature\logs\install-cli-*.log`
- `%TEMP%\Setup Log*.txt` — лог Inno Setup (искать `marker=tray-logon-task`, `marker=uninstall-app-directory`, `marker=install-health`)

## Временный workaround

```powershell
# admin
.\scripts\repair-upgrade.ps1
```

или для Melezh отдельно:

```powershell
.\scripts\repair-melezh.ps1
```

## Постоянный фикс (в коде)

- Устойчивое ожидание удаления сервисов в SCM (`retry + backoff + повторный stop/delete`).
- Смягчение pre-create fail-hard: попытка пройти `sc create` retry даже при still-present состоянии.
- Second-pass uninstall очистка сервисов перед degraded verdict.
- Best-effort автозапуск `AriaSignatureMelezhService` в UI startup/recovery.
- Расширенная диагностика `sc queryex/qc` (StartType, binPath, pending delete).
- Tray logon task: `BuildTrayLogonDomainUser()` через `{%USERDOMAIN%}\{%USERNAME%}`, `try/except`, без исключений из post-install.
- Post-install: `RaiseException` только при `install-health:fail-hard*`; Melezh UI probe → `degraded-melezh-ui`; degraded/timeout без MsgBox пользователю.
- OInt bundle: патч `installer/melezh/oint-http-index/http.json` в `prepare-melezh.ps1`; bootstrap schema v2 (`MelezhHost --bootstrap-only`, `repair-melezh.ps1`).
- Uninstall: `taskkill` + `RemoveAppDirectoryBestEffort` в `usPostUninstall`.
- `release-gate`: запрет `{userdomain}` / `{domainuser}` в `AriaSignature.iss`.

## Критерии «исправлено»

- uninstall удаляет `AriaSignatureService` и `AriaSignatureMelezhService` стабильно;
- uninstall удаляет каталог `{app}` (или показывает понятное предупреждение с путём);
- install не падает преждевременно на `still present in SCM before create`;
- install завершается без `Unknown constant "userdomain"`; кнопка **Завершить** закрывает мастер;
- после старта UI Melezh запускается без ручного repair;
- `http://127.0.0.1:7788/ui` доступен после штатного старта (или восстанавливается после прогрева / repair);
- `GET http://127.0.0.1:7788/aria_ping` и `aria_get_status` возвращают JSON без `result:false` (ошибка «http» устранена).

