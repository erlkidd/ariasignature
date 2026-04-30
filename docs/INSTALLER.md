# AriaSignature Installer Guide

## Prerequisites

- Windows 10/11 x64.
- Inno Setup 6 installed.
- .NET publish artifacts prepared for UI and Service projects.

## Build publish artifacts

Run from repository root:

```powershell
dotnet publish .\src\ui\AriaSignature.UI\AriaSignature.UI.csproj -c Release -o .\publish\ui
dotnet publish .\src\service\AriaSignature.Service\AriaSignature.Service.csproj -c Release -o .\publish\service
```

## Build installer package

- Open `installer/inno/AriaSignature.iss` in Inno Setup Compiler.
- Build the installer.
- Output package appears in `artifacts/installer`.
- For automated RC run use: `.\scripts\release-gate.ps1`.

## Elevation and permissions

- Installer requires administrator rights (`PrivilegesRequired=admin`).
- Service is installed with auto-start and restart-on-failure policy.
- Uninstaller stops/deletes service and removes installed files.

## Service behavior during install

- Installer performs deterministic stop/delete of old `AriaSignatureService` instance before re-registering (upgrade-safe idempotent flow).
- Service is re-created with auto-start and recovery policy (`restart`).
- Service starts immediately after installation; setup stops with an explicit error if service registration/start fails.

## Service behavior during uninstall

- Uninstall executes best-effort stop/delete for `AriaSignatureService`.
- Missing-service and already-stopped states are treated as acceptable (idempotent uninstall path).
- Runtime SQLite/log data inside installation directory is removed together with `{app}`.

## Tray autostart behavior

- Installer can add a startup shortcut in `commonstartup`.
- Startup shortcut launches UI with `--tray`.
- Closing window sends app to tray; full exit is available from tray menu.

## Icon notes

- Source visual asset is root `icon.png`.
- Unified `icon.ico` is used for exe/window/tray/shortcuts/installer.
- `SetupIconFile` is enabled in `AriaSignature.iss`.
