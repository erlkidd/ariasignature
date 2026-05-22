# OInt / Melezh bundle for AriaSignature installer

## Build-time layout

| Path | Purpose |
|------|---------|
| `oint_2.0.0_installer_ru.exe` | Official OpenIntegrations Windows installer (NSIS, silent `/S`) |
| `bundle/` | **Generated** by `scripts/prepare-melezh.ps1` — copied into `{app}\melezh` by Inno Setup |
| `VERSION` | Optional pin note for docs |

`bundle/` is not committed (see root `.gitignore`).

`required-files.json` — manifest checked by `scripts/Test-MelezhBundle.ps1` and `release-gate`.

## Prepare before installer build

```powershell
.\scripts\prepare-melezh.ps1
```

The script:

1. Runs `oint_*_installer_ru.exe /S` (installs to `%ProgramFiles(x86)%\OInt`).
2. Mirrors the tree into `installer/melezh/bundle/` (without `unins000.exe`).
3. Uninstalls the temporary system copy when possible.

Override source tree: set `ARIASIGNATURE_MELEZH_DIR` to an existing OInt root (must contain `bin\melezh.bat`).

## Runtime layout after AriaSignature setup

```
{app}\melezh\bin\melezh.bat
{app}\melezh\lib\oint\bin\oscript.exe
{app}\melezh\share\oint\...
```

`AriaSignatureMelezhService` launches `bin\melezh.bat` via `AriaSignature.MelezhHost`.
