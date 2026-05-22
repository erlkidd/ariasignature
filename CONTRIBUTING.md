# Contributing to AriaSignature

Спасибо за интерес к проекту. Документация: [`NAVIGATION-DOCS.MD`](NAVIGATION-DOCS.MD).

## Требования

- Windows 10/11 x64 (целевая платформа).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- [Node.js](https://nodejs.org/) 20+ (сборка SPA).
- Inno Setup 6 — для установщика (см. `docs/INSTALLER.md`).

## Ветки и remotes

1. Рабочая ветка: **`test/agent-work`** (фичи, исправления, документация).
2. После review и зелёного gate: merge в **`production`**.
3. Push на оба remote: **`origin`** (GitFlic), **`old-origin`** (GitHub).

Не делайте force-push в `production` без согласования.

## Сборка и проверки

```powershell
.\scripts\release-gate.ps1
```

Артефакт: `artifacts/installer/AriaSignature-Setup.exe`.

Отдельно (быстрее):

```powershell
dotnet test .\AriaSignature.slnx -c Release
.\scripts\Test-ApiDocParity.ps1
.\scripts\Test-MelezhCatalogParity.ps1
```

## Чеклист при изменении API

| Изменение | Обновить |
|-----------|----------|
| Новый/изменённый route | `src/api/AriaSignature.Api/AriaApiExtensions.cs` |
| Документация REST | `docs/API.md` (и §7 матрица при смене UI/Melezh) |
| Parity baseline | `scripts/Test-ApiDocParity.ps1` при добавлении маршрута |
| Melezh handler | `MelezhAriaApiHandlerCatalog.cs`, `docs/MELEZH_HANDLER_CATALOG.md` |
| Bootstrap schema | `MelezhBootstrapSchema.cs`, `docs/MELEZH.md` |
| Релиз | `VERSION`, `.\scripts\sync-project-version.ps1`, `docs/CHANGELOG.md` |

## Чеклист при изменении UI (SPA)

- `src/web/src/App.tsx` — вызовы `/api/v1/*` должны соответствовать `docs/API.md`.
- `npm run build` в `src/web` (входит в release-gate).

## Коммиты

Краткие сообщения в стиле:

- `feat(scope): …`
- `fix(scope): …`
- `docs: …`

## Не коммитить

- Секреты, `.env`, пароли MSSQL из тестовых машин.
- Локальные БД `%ProgramData%\AriaSignature\*.db`.
- `installer/melezh/bundle/` (собирается `prepare-melezh.ps1`).
- Папку `skills/` (см. `.gitignore`).

## Лицензия

Вклад принимается под [MIT](LICENSE).
