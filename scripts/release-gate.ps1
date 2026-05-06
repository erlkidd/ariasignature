param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$RequireInstalledServiceSmoke
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot\..")
$startedAt = Get-Date

function Write-Step {
    param(
        [int]$Index,
        [int]$Total,
        [string]$Name
    )
    Write-Host ("[release-gate][step-start] index={0}/{1} name=""{2}"" utc={3}" -f $Index, $Total, $Name, (Get-Date).ToUniversalTime().ToString("o"))
}

function Assert-ExitCode {
    param(
        [int]$Code,
        [string]$Operation
    )
    if ($Code -ne 0) {
        throw "[release-gate][step-fail] operation=""$Operation"" exit_code=$Code"
    }
}

function Invoke-InstalledServiceSmokeIfPresent {
    param(
        [string]$ServiceName = "AriaSignatureService",
        [int]$ApiPort = 5160,
        [switch]$FailOnError
    )

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Host "[release-gate][check] installed service smoke skipped: service '$ServiceName' not found on this host"
        return
    }

    Write-Host "[release-gate][check] running installed service smoke for '$ServiceName'"
    powershell -ExecutionPolicy Bypass -File ".\scripts\service-startup-smoke.ps1" -ServiceName $ServiceName -ApiPort $ApiPort -WaitSeconds 45
    if ($LASTEXITCODE -eq 0) {
        return
    }

    if ($FailOnError) {
        Assert-ExitCode -Code $LASTEXITCODE -Operation "service-startup-smoke"
        return
    }

    Write-Host "[release-gate][check] installed service smoke reported failures; continuing because -RequireInstalledServiceSmoke is not set"
}

function Stop-RepoLockedProcess {
    param(
        [string]$ProcessName,
        [string]$LockedRoot
    )

    $targetRoot = [System.IO.Path]::GetFullPath($LockedRoot).TrimEnd('\')
    $escapedName = $ProcessName.Replace("'", "''")
    $candidates = Get-CimInstance Win32_Process -Filter ("Name = '{0}'" -f $escapedName) -ErrorAction SilentlyContinue
    if (-not $candidates) {
        return
    }

    foreach ($proc in $candidates) {
        $procId = [int]$proc.ProcessId
        $exePath = $proc.ExecutablePath
        if ([string]::IsNullOrWhiteSpace($exePath)) {
            continue
        }

        $fullExePath = [System.IO.Path]::GetFullPath($exePath)
        if (-not $fullExePath.StartsWith($targetRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        Write-Host ("[release-gate][check] stopping lock holder pid={0} exe=""{1}""" -f $procId, $fullExePath)
        $service = Get-CimInstance Win32_Service -Filter ("ProcessId = {0}" -f $procId) -ErrorAction SilentlyContinue
        if ($service) {
            Write-Host ("[release-gate][check] stopping service name=""{0}"" for pid={1}" -f $service.Name, $procId)
            & sc.exe stop $service.Name | Out-Null
            Start-Sleep -Seconds 2
        }

        try {
            $stillRunning = Get-Process -Id $procId -ErrorAction SilentlyContinue
            if ($stillRunning) {
                Stop-Process -Id $procId -Force -ErrorAction Stop
                Start-Sleep -Milliseconds 500
            }
        }
        catch {
            throw "[release-gate][step-fail] operation=""release-publish-preflight"" reason=""failed-to-stop-lock-holder"" pid=$procId process=""$ProcessName"""
        }
    }
}

Write-Step -Index 1 -Total 9 -Name "build-web-ui"
Push-Location .\src\web
npm ci
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "[release-gate][step-fail] operation=""npm-ci"" exit_code=$LASTEXITCODE" }
npm run build
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "[release-gate][step-fail] operation=""npm-build"" exit_code=$LASTEXITCODE" }
Pop-Location

Write-Step -Index 2 -Total 9 -Name "build-solution"
dotnet build .\AriaSignature.slnx -c $Configuration
Assert-ExitCode -Code $LASTEXITCODE -Operation "dotnet-build"

Write-Step -Index 3 -Total 9 -Name "run-tests"
dotnet test .\AriaSignature.slnx -c $Configuration
Assert-ExitCode -Code $LASTEXITCODE -Operation "dotnet-test"

Write-Step -Index 4 -Total 9 -Name "publish-ui"
Stop-RepoLockedProcess -ProcessName "AriaSignature.UI.exe" -LockedRoot (Join-Path (Get-Location) "publish\ui")
Remove-Item -Path .\publish\ui -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish .\src\ui\AriaSignature.UI\AriaSignature.UI.csproj -c $Configuration -r $RuntimeIdentifier --self-contained true -o .\publish\ui
Assert-ExitCode -Code $LASTEXITCODE -Operation "dotnet-publish-ui"

Write-Host "[release-gate][check] bootstrap package source = publish/bootstrap"
$bootstrapPublishDir = ".\publish\bootstrap"
Remove-Item -Path $bootstrapPublishDir -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish .\src\tools\AriaSignature.ServiceBootstrap\AriaSignature.ServiceBootstrap.csproj -c $Configuration -r $RuntimeIdentifier --self-contained true -o $bootstrapPublishDir
Assert-ExitCode -Code $LASTEXITCODE -Operation "dotnet-publish-bootstrap"
if (-not (Test-Path (Join-Path $bootstrapPublishDir "AriaSignature.ServiceBootstrap.exe"))) {
    throw "[release-gate][step-fail] operation=""verify-publish-bootstrap"" reason=""bootstrap-exe-missing-in-bootstrap-publish"""
}
$bootstrapInUiDir = ".\publish\ui\bootstrap"
Remove-Item -Path $bootstrapInUiDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -Path $bootstrapInUiDir -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $bootstrapPublishDir "*") -Destination $bootstrapInUiDir -Recurse -Force

$uiExe = ".\publish\ui\AriaSignature.UI.exe"
$bootstrapExe = ".\publish\ui\bootstrap\AriaSignature.ServiceBootstrap.exe"
if (-not (Test-Path $uiExe)) { throw "[release-gate][step-fail] operation=""verify-publish-ui"" reason=""ui-exe-missing""" }
if (-not (Test-Path $bootstrapExe)) { throw "[release-gate][step-fail] operation=""verify-publish-ui"" reason=""bootstrap-exe-missing""" }
$bootstrapHostFxr = ".\publish\ui\bootstrap\hostfxr.dll"
$bootstrapHostPolicy = ".\publish\ui\bootstrap\hostpolicy.dll"
if (-not (Test-Path $bootstrapHostFxr)) { throw "[release-gate][step-fail] operation=""verify-bootstrap-self-contained"" reason=""hostfxr-missing""" }
if (-not (Test-Path $bootstrapHostPolicy)) { throw "[release-gate][step-fail] operation=""verify-bootstrap-self-contained"" reason=""hostpolicy-missing""" }

Write-Step -Index 5 -Total 9 -Name "publish-service"
Stop-RepoLockedProcess -ProcessName "AriaSignature.Service.exe" -LockedRoot (Join-Path (Get-Location) "publish\service")
Stop-RepoLockedProcess -ProcessName "AriaSignature.Api.exe" -LockedRoot (Join-Path (Get-Location) "publish\service")
Remove-Item -Path .\publish\service -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish .\src\service\AriaSignature.Service\AriaSignature.Service.csproj -c $Configuration -r $RuntimeIdentifier --self-contained true -o .\publish\service
Assert-ExitCode -Code $LASTEXITCODE -Operation "dotnet-publish-service"
$serviceExe = ".\publish\service\AriaSignature.Service.exe"
if (-not (Test-Path $serviceExe)) { throw "[release-gate][step-fail] operation=""verify-publish-service"" reason=""service-exe-missing""" }
$serviceDeps = ".\publish\service\AriaSignature.Service.deps.json"
$serviceRuntimeConfig = ".\publish\service\AriaSignature.Service.runtimeconfig.json"
if (-not (Test-Path $serviceDeps)) { throw "[release-gate][step-fail] operation=""verify-publish-service"" reason=""service-deps-missing""" }
if (-not (Test-Path $serviceRuntimeConfig)) { throw "[release-gate][step-fail] operation=""verify-publish-service"" reason=""service-runtimeconfig-missing""" }
$depsRaw = Get-Content -Path $serviceDeps -Raw
$ridPattern = "/" + [regex]::Escape($RuntimeIdentifier) + '"'
if ($depsRaw -notmatch $ridPattern) {
    throw "[release-gate][step-fail] operation=""verify-service-runtime-target"" reason=""rid-mismatch-in-deps"" expected=""$RuntimeIdentifier"""
}
if ($depsRaw -notmatch '"runtimeTarget"\s*:\s*\{\s*"name"\s*:\s*"\.NETCoreApp,Version=v8\.0/' + [regex]::Escape($RuntimeIdentifier) + '"') {
    throw "[release-gate][step-fail] operation=""verify-service-runtime-target"" reason=""runtime-target-missing-in-deps"" expected=""$RuntimeIdentifier"""
}
if ($depsRaw -notmatch '"Microsoft\.Extensions\.Hosting\.WindowsServices"\s*:\s*"') {
    throw "[release-gate][step-fail] operation=""verify-service-runtime-target"" reason=""windowsservices-dependency-missing"""
}
if ($depsRaw -notmatch '"Microsoft\.Extensions\.Hosting\.WindowsServices"\s*:\s*"8\.') {
    throw "[release-gate][step-fail] operation=""verify-service-runtime-target"" reason=""windowsservices-version-not-net8-compatible"""
}

Write-Step -Index 6 -Total 9 -Name "prepare-webview2"
$webView2Dir = ".\installer\webview2"
$webView2Exe = Join-Path $webView2Dir "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
$webView2OfflineUrl = "https://go.microsoft.com/fwlink/?linkid=2124703"
$webView2MinimumBytes = 50MB
if (-not (Test-Path $webView2Exe)) {
    New-Item -Path $webView2Dir -ItemType Directory -Force | Out-Null
    Write-Host "Downloading WebView2 standalone offline runtime installer from Microsoft..."
    Invoke-WebRequest -Uri $webView2OfflineUrl -OutFile $webView2Exe
}

$webView2File = Get-Item -Path $webView2Exe -ErrorAction Stop
if ($webView2File.Length -lt $webView2MinimumBytes) {
    throw "[release-gate][step-fail] operation=""prepare-webview2"" reason=""webview2-installer-too-small"" bytes=$($webView2File.Length) min_bytes=$webView2MinimumBytes"
}

$webView2Signature = Get-AuthenticodeSignature -FilePath $webView2Exe
if ($webView2Signature.Status -ne "Valid") {
    throw "[release-gate][step-fail] operation=""prepare-webview2"" reason=""webview2-signature-invalid"" status=""$($webView2Signature.Status)"""
}
if ($webView2Signature.SignerCertificate.Subject -notmatch "Microsoft") {
    throw "[release-gate][step-fail] operation=""prepare-webview2"" reason=""webview2-signer-unexpected"" subject=""$($webView2Signature.SignerCertificate.Subject)"""
}

Write-Step -Index 7 -Total 9 -Name "prepare-smartctl"
$smartCtlDir = ".\installer\smartctl"
$smartCtlExe = Join-Path $smartCtlDir "smartctl.exe"
$driveDbPath = Join-Path $smartCtlDir "drivedb.h"
if (-not (Test-Path $smartCtlExe)) {
    $smartCtlCmd = Get-Command smartctl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    if ($smartCtlCmd) {
        New-Item -Path $smartCtlDir -ItemType Directory -Force | Out-Null
        Copy-Item -Path $smartCtlCmd -Destination $smartCtlExe -Force
        $cmdDir = Split-Path -Parent $smartCtlCmd
        $pathDriveDb = Join-Path $cmdDir "drivedb.h"
        if (Test-Path $pathDriveDb) {
            Copy-Item -Path $pathDriveDb -Destination $driveDbPath -Force
        }
    }
    else {
        $tmpRoot = ".\installer\smartctl-temp"
        $extractRoot = Join-Path $tmpRoot "extract"
        $setupExe = Join-Path $tmpRoot "smartmontools-setup.exe"
        New-Item -Path $tmpRoot -ItemType Directory -Force | Out-Null
        $downloadUrl = "https://github.com/smartmontools/smartmontools/releases/download/RELEASE_7_5/smartmontools-7.5.win32-setup.exe"
        Write-Host "Downloading smartmontools setup from GitHub releases..."
        Invoke-WebRequest -Uri $downloadUrl -OutFile $setupExe

        $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
        if (-not $sevenZip) {
            throw "7z not found. Install 7-Zip CLI or provide smartctl.exe in PATH/installer\\smartctl."
        }

        Remove-Item -Path $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -Path $extractRoot -ItemType Directory -Force | Out-Null
        & $sevenZip x $setupExe "-o$((Resolve-Path $extractRoot).Path)" -y | Out-Null

        $candidate = Join-Path $extractRoot "bin\smartctl.exe"
        if (-not (Test-Path $candidate)) {
            throw "Failed to extract smartctl.exe from smartmontools setup package."
        }

        New-Item -Path $smartCtlDir -ItemType Directory -Force | Out-Null
        Copy-Item -Path $candidate -Destination $smartCtlExe -Force
        $drivedb = Join-Path $extractRoot "bin\drivedb.h"
        if (Test-Path $drivedb) {
            Copy-Item -Path $drivedb -Destination $driveDbPath -Force
        }
    }
}

if (-not (Test-Path $smartCtlExe)) {
    throw "smartctl.exe is missing after prepare step."
}
if ((Get-Item $smartCtlExe).Length -le 0) {
    throw "smartctl.exe is empty after prepare step."
}
if (-not (Test-Path $driveDbPath)) {
    throw "drivedb.h is missing after prepare step."
}
if ((Get-Item $driveDbPath).Length -le 0) {
    throw "drivedb.h is empty after prepare step."
}

Write-Host "[release-gate][check] validating host dependencies"
$requiredCommands = @("powershell.exe", "sc.exe", "taskkill.exe")
foreach ($cmd in $requiredCommands) {
    $resolved = Get-Command $cmd -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    if (-not $resolved) {
        throw "[release-gate][step-fail] operation=""host-dependency-check"" missing=""$cmd"""
    }
}

$logsRoot = Join-Path $env:ProgramData "AriaSignature\logs"
New-Item -Path $logsRoot -ItemType Directory -Force | Out-Null
$probeLog = Join-Path $logsRoot "release-gate-write-test.log"
"ok" | Out-File -FilePath $probeLog -Encoding utf8 -Force
Remove-Item -Path $probeLog -Force -ErrorAction SilentlyContinue

Write-Step -Index 8 -Total 9 -Name "installed-service-smoke"
Invoke-InstalledServiceSmokeIfPresent -ServiceName "AriaSignatureService" -ApiPort 5160 -FailOnError:$RequireInstalledServiceSmoke

Write-Step -Index 9 -Total 9 -Name "build-installer"
Stop-RepoLockedProcess -ProcessName "AriaSignature-Setup.exe" -LockedRoot (Join-Path (Get-Location) "artifacts\installer")
$isccPath = Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
if (-not $isccPath) {
    $fallback = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (Test-Path $fallback) {
        $isccPath = $fallback
    }
}

if (-not $isccPath) {
    throw "ISCC.exe not found. Install Inno Setup 6 or add ISCC to PATH."
}

& $isccPath .\installer\inno\AriaSignature.iss
Assert-ExitCode -Code $LASTEXITCODE -Operation "iscc-build-installer"

$elapsed = [int]((Get-Date) - $startedAt).TotalSeconds
Write-Host ("[release-gate][done] status=success elapsed_sec={0} utc={1}" -f $elapsed, (Get-Date).ToUniversalTime().ToString("o"))
