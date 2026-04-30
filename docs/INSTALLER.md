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

## Service behavior during install

- Installer registers `AriaSignatureService` as Windows Service.
- Service is configured for auto-start.
- Service starts immediately after installation.

## Icon notes

- Source visual asset is root `icon.png`.
- Tray icon already uses this asset at runtime.
- For installer executable icon, convert `icon.png` to `icon.ico` and set `SetupIconFile` in the script.
