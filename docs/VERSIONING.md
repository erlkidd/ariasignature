# Версионирование AriaSignature

Версия документа: 1.1.2

## Источник правды

Файл [`VERSION`](../VERSION) в корне репозитория (одна строка, например `1.1.0`).

## Синхронизация

```powershell
.\scripts\sync-project-version.ps1
```

Обновляет:

- `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
- `#define MyAppVersion` в `installer/inno/AriaSignature.iss`
- строки «Версия документа» в `docs/*.md`

## Релиз

1. Изменить `VERSION`.
2. Запустить `sync-project-version.ps1`.
3. Обновить `CHANGELOG.md`.
4. `.\scripts\release-gate.ps1` на ветке `test/agent-work`.
5. Commit + push на `origin` и `old-origin`.
