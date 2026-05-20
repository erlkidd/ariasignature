# Mirrors installer/inno/AriaSignature.iss post-install / uninstall SCM logic for install-cli.ps1 (agent testing).
# Production installer uses Inno Setup Pascal only — do not call this from AriaSignature-Setup.exe.

$script:InstallHealthStatus = 'install-health:pending'
$script:LastScExitCode = 0
$script:LastProbeExitCode = 0

function Set-IssHealthStatus {
    param([string]$Status)
    $script:InstallHealthStatus = $Status
    Write-AriaInstallLog "marker=install-health status=$Status"
}

function Test-IssServiceRegistered {
    param([string]$ServiceName)
    & sc.exe query $ServiceName 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Invoke-IssSc {
    param(
        [string]$Arguments,
        [int[]]$AcceptExitCodes = @(0)
    )
    $r = Invoke-AriaSc -Arguments $Arguments -AcceptExitCodes $AcceptExitCodes
    $script:LastScExitCode = $r.Code
    return $r.Ok
}

function Invoke-IssPreInstallCleanup {
    param([string]$InstallRoot)
    $cfg = Get-AriaInstallConfig
    Write-AriaInstallLog "stage=pre-install-cleanup (iss RunPreInstallCleanup)"
    Invoke-IssStopAndDeleteBothBestEffort
    Stop-AriaProcesses
    for ($i = 1; $i -le 3; $i++) {
        Invoke-IssSc "stop $($cfg.mainServiceName)" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
        Invoke-IssSc "stop $($cfg.melezhServiceName)" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
        Stop-AriaProcesses
        Start-Sleep -Seconds 1
    }
    if (-not (Wait-AriaServiceAbsent -ServiceName $cfg.mainServiceName -TimeoutSeconds 15)) {
        Write-AriaInstallLog "WARN: $($cfg.mainServiceName) still in SCM before file copy equivalent"
    }
    if (-not (Wait-AriaServiceAbsent -ServiceName $cfg.melezhServiceName -TimeoutSeconds 15)) {
        Write-AriaInstallLog "WARN: $($cfg.melezhServiceName) still in SCM before file copy equivalent"
    }
}

function Invoke-IssStopAndDeleteBothBestEffort {
    $cfg = Get-AriaInstallConfig
    foreach ($name in @($cfg.melezhServiceName, $cfg.mainServiceName)) {
        Invoke-IssSc "stop $name" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
        Invoke-IssSc "delete $name" -AcceptExitCodes @(0, 1060, 1072) | Out-Null
    }
}

function Assert-IssMelezhBundleOrAbort {
    param([string]$InstallRoot)
    $cfg = Get-AriaInstallConfig
    $paths = @(
        (Join-Path $InstallRoot $cfg.paths.melezhBat),
        (Join-Path $InstallRoot $cfg.paths.oscriptExe),
        (Join-Path $InstallRoot $cfg.paths.melezhHostExe)
    )
    foreach ($p in $paths) {
        if (-not (Test-Path $p)) {
            throw "ISS preflight missing: $p"
        }
    }
    $ointCli = Join-Path $InstallRoot "melezh\share\oint\lib\oint-cli"
    if (-not (Test-Path $ointCli)) {
        throw "ISS preflight missing: melezh\share\oint\lib\oint-cli"
    }
    Test-MelezhBundleComplete -MelezhRoot (Join-Path $InstallRoot "melezh")
}

function Invoke-IssBootstrapRepair {
    param(
        [string]$InstallRoot,
        [string]$ServiceExePath
    )
    $bootstrap = Join-Path $InstallRoot "ui\bootstrap\AriaSignature.ServiceBootstrap.exe"
    if (-not (Test-Path $bootstrap)) {
        Write-AriaInstallLog "Auto-repair skipped: bootstrap missing"
        return $false
    }
    Write-AriaInstallLog "Auto-repair: launching bootstrap helper"
    $proc = Start-Process -FilePath $bootstrap -ArgumentList "`"$ServiceExePath`"" -Wait -PassThru -NoNewWindow -WindowStyle Hidden
    Write-AriaInstallLog "Auto-repair bootstrap exit code: $($proc.ExitCode)"
    return $proc.ExitCode -eq 0
}

function Test-IssApiProbe {
    param([int]$Port = 5160)
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/api/v1/status" -UseBasicParsing -TimeoutSec 3
        $script:LastProbeExitCode = $r.StatusCode
        return $r.StatusCode -eq 200
    }
    catch {
        $script:LastProbeExitCode = 1
        return $false
    }
}

function Get-IssApiReportedVersion {
    param([int]$Port = 5160)
    try {
        $s = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/api/v1/status" -TimeoutSec 5
        return [string]$s.version
    }
    catch {
        return ""
    }
}

function Test-IssVersionMatchesExpected {
    param([string]$Actual, [string]$Expected)
    if ([string]::IsNullOrWhiteSpace($Actual)) { return $false }
    return $Actual -match [regex]::Escape($Expected)
}

function Install-IssMainService {
    param(
        [string]$InstallRoot,
        [string]$ExpectedVersion
    )
    $cfg = Get-AriaInstallConfig
    $name = $cfg.mainServiceName
    $bin = Join-Path $InstallRoot $cfg.paths.serviceExe
    Set-IssHealthStatus 'install-health:starting'

    foreach ($label in @('UI exe', 'service exe', 'bootstrap exe', 'smartctl.exe', 'drivedb.h')) {
        # ISS AssertFileExistsOrAbort preflight subset
    }
    $preflight = @(
        (Join-Path $InstallRoot $cfg.paths.uiExe),
        $bin,
        (Join-Path $InstallRoot $cfg.paths.bootstrapExe),
        (Join-Path $InstallRoot $cfg.paths.smartctlExe),
        (Join-Path $InstallRoot $cfg.paths.drivedb)
    )
    foreach ($p in $preflight) {
        if (-not (Test-Path $p)) {
            throw "ISS preflight missing: $p"
        }
    }

    Invoke-IssStopAndDeleteBothBestEffort
    Stop-AriaProcesses
    if (-not (Wait-AriaServiceAbsent -ServiceName $name -TimeoutSeconds 20)) {
        Set-IssHealthStatus 'install-health:degraded-service-stuck-precreate'
        Write-AriaInstallLog "WARN: ISS $name still in SCM before create; continuing with create retries"
        Invoke-IssSc "stop $name" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
        Invoke-IssSc "delete $name" -AcceptExitCodes @(0, 1060, 1072) | Out-Null
        Stop-AriaProcesses
        Start-Sleep -Seconds 2
    }

    $quoted = "`"$bin`""
    $createVariants = @(
        "create $name binPath= $quoted start= auto DisplayName= `"AriaSignature`" obj= LocalSystem",
        "create $name binPath= $quoted start= auto DisplayName= `"AriaSignature Service`" obj= LocalSystem",
        "create $name binPath= $quoted start= auto obj= LocalSystem"
    )

    $created = $false
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        Write-AriaInstallLog "ISS create-service attempt $attempt/20"
        foreach ($create in $createVariants) {
            if (Invoke-IssSc $create) {
                $created = $true
                break
            }
            if ($script:LastScExitCode -eq 1078) { continue }
        }
        if ($created) { break }

        if ($script:LastScExitCode -in 1073, 1072) {
            Invoke-IssSc "stop $name" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
            Invoke-IssSc "delete $name" -AcceptExitCodes @(0, 1060, 1072) | Out-Null
            Stop-AriaProcesses
            Wait-AriaServiceAbsent -ServiceName $name -TimeoutSeconds 15 | Out-Null
            Start-Sleep -Seconds 2
        }
        elseif ($script:LastScExitCode -eq 1072) {
            Wait-AriaServiceAbsent -ServiceName $name -TimeoutSeconds 15 | Out-Null
            Start-Sleep -Seconds 2
        }
        else {
            Start-Sleep -Seconds 1
        }
    }

    if (-not $created) {
        Set-IssHealthStatus 'install-health:degraded-service-create-failed'
        Write-AriaInstallLog "WARN: ISS degraded — service create failed sc=$($script:LastScExitCode)"
        return
    }

    if (-not (Test-IssServiceRegistered $name)) {
        Set-IssHealthStatus 'install-health:degraded-service-not-registered'
        Write-AriaInstallLog "WARN: ISS degraded — service not in SCM"
        return
    }

    Invoke-IssSc "failure $name reset= 86400 actions= restart/5000/restart/5000/restart/5000" -AcceptExitCodes @(0, -1) | Out-Null

    $started = $false
    for ($attempt = 1; $attempt -le 12; $attempt++) {
        if (Invoke-IssSc "start $name" -AcceptExitCodes @(0, 1056)) {
            $started = $true
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $started) {
        if (Invoke-IssBootstrapRepair -InstallRoot $InstallRoot -ServiceExePath $bin) {
            try {
                $s = Get-Service -Name $name -ErrorAction Stop
                $started = $s.Status -eq 'Running'
            }
            catch { $started = $false }
        }
    }

    if (-not $started) {
        Set-IssHealthStatus 'install-health:degraded-service-not-running'
        Write-AriaInstallLog "WARN: ISS degraded — service not running sc=$($script:LastScExitCode)"
        return
    }

    $healthy = $false
    for ($p = 1; $p -le 8; $p++) {
        if (Test-IssApiProbe -Port $cfg.apiPort) {
            $healthy = $true
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $healthy) {
        if (Invoke-IssBootstrapRepair -InstallRoot $InstallRoot -ServiceExePath $bin) {
            $healthy = Test-IssApiProbe -Port $cfg.apiPort
        }
    }

    if (-not $healthy) {
        Set-IssHealthStatus 'install-health:degraded-api-warmup'
        Write-AriaInstallLog "WARN: ISS degraded — API not ready"
        return
    }

    $apiVer = Get-IssApiReportedVersion -Port $cfg.apiPort
    $onDisk = Get-FileProductVersionSafe -Path (Join-Path $InstallRoot $cfg.paths.apiExe)
    if (-not (Test-IssVersionMatchesExpected -Actual $apiVer -Expected $ExpectedVersion)) {
        Set-IssHealthStatus 'install-health:fail-hard-version'
        throw "ISS fail-hard-version: expected=$ExpectedVersion api=$apiVer onDisk=$onDisk"
    }

    Set-IssHealthStatus 'install-health:ok'
}

function Install-IssMelezhService {
    param([string]$InstallRoot)
    $cfg = Get-AriaInstallConfig
    $name = $cfg.melezhServiceName
    $bin = Join-Path $InstallRoot $cfg.paths.melezhHostExe

    Assert-IssMelezhBundleOrAbort -InstallRoot $InstallRoot

    Invoke-IssStopAndDeleteBothBestEffort
    Stop-AriaProcesses
    Wait-AriaServiceAbsent -ServiceName $name -TimeoutSeconds 12 | Out-Null

    $quoted = "`"$bin`""
    $create = "create $name binPath= $quoted start= auto DisplayName= `"AriaSignature Melezh`" obj= LocalSystem"
    $created = $false

    for ($attempt = 1; $attempt -le 12; $attempt++) {
        Write-AriaInstallLog "ISS create-melezh-service attempt $attempt/12"
        if (Invoke-IssSc $create) {
            $created = $true
            break
        }
        if ($script:LastScExitCode -in 1073, 1072) {
            Invoke-IssSc "stop $name" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
            Invoke-IssSc "delete $name" -AcceptExitCodes @(0, 1060, 1072) | Out-Null
            Stop-AriaProcesses
            Wait-AriaServiceAbsent -ServiceName $name -TimeoutSeconds 10 | Out-Null
            Start-Sleep -Seconds 1
        }
        else {
            Start-Sleep -Seconds 1
        }
    }

    if (-not $created) {
        Set-IssHealthStatus 'install-health:fail-hard-melezh'
        throw "ISS fail-hard-melezh: sc create code $($script:LastScExitCode)"
    }

    if (-not (Test-IssServiceRegistered $name)) {
        Set-IssHealthStatus 'install-health:fail-hard-melezh'
        throw "ISS fail-hard-melezh: not registered after create"
    }

    Invoke-IssSc "description $name `"OpenIntegrations Melezh HTTP gateway for AriaSignature`"" | Out-Null
    Invoke-IssSc "failure $name reset= 86400 actions= restart/60000/restart/60000/restart/60000" | Out-Null

    $started = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        if (Invoke-IssSc "start $name" -AcceptExitCodes @(0, 1056)) {
            $started = $true
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $started) {
        Set-IssHealthStatus 'install-health:fail-hard-melezh'
        throw "ISS fail-hard-melezh: sc start code $($script:LastScExitCode)"
    }

    $healthy = $false
    for ($p = 1; $p -le 10; $p++) {
        if (Test-MelezhUiHealth -Port $cfg.melezhPort -MaxAttempts 1 -TimeoutSec 5) {
            $healthy = $true
            break
        }
        Start-Sleep -Seconds 2
    }

    if (-not $healthy) {
        Set-IssHealthStatus 'install-health:fail-hard-melezh'
        throw "ISS fail-hard-melezh: Web UI not responding on :$($cfg.melezhPort)/ui"
    }

    Write-AriaInstallLog 'marker=install-health stage=melezh-ready status=ok'
}

function Uninstall-IssServices {
    Write-AriaInstallLog 'stage=uninstall (iss CurUninstallStepChanged)'
    $cfg = Get-AriaInstallConfig
    for ($i = 1; $i -le 3; $i++) {
        Invoke-IssSc "stop $($cfg.mainServiceName)" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
        Invoke-IssSc "stop $($cfg.melezhServiceName)" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
        Stop-AriaProcesses
        Start-Sleep -Seconds 1
    }
    Invoke-IssStopAndDeleteBothBestEffort
    Stop-AriaProcesses
    $okM = Wait-AriaServiceAbsent -ServiceName $cfg.melezhServiceName -TimeoutSeconds 25
    $okA = Wait-AriaServiceAbsent -ServiceName $cfg.mainServiceName -TimeoutSeconds 25
    if ($okM -and $okA) {
        Write-AriaInstallLog 'marker=uninstall-service-removal status=ok'
    }
    else {
        Write-AriaInstallLog "WARN: uninstall first-pass timed out; running second-pass"
        Invoke-IssStopAndDeleteBothBestEffort
        Stop-AriaProcesses
        $okM = Wait-AriaServiceAbsent -ServiceName $cfg.melezhServiceName -TimeoutSeconds 60
        $okA = Wait-AriaServiceAbsent -ServiceName $cfg.mainServiceName -TimeoutSeconds 60
        if ($okM -and $okA) {
            Write-AriaInstallLog 'marker=uninstall-service-removal status=ok mode=second-pass'
        }
        else {
            Write-AriaInstallLog 'marker=uninstall-service-removal status=degraded reason=service-still-present-or-pending'
            throw "ISS uninstall degraded: services still in SCM (main=$okA melezh=$okM)"
        }
    }
}

function Invoke-IssMirrorInstall {
    param(
        [string]$InstallRoot,
        [string]$ExpectedVersion
    )
    # Order matches AriaSignature.iss: ssInstall cleanup, then ssPostInstall steps.
    Invoke-IssPreInstallCleanup -InstallRoot $InstallRoot

    $apiExe = Join-Path $InstallRoot (Get-AriaInstallConfig).paths.apiExe
    Assert-OnDiskVersion -ApiExePath $apiExe -ExpectedVersion $ExpectedVersion

    Assert-IssMelezhBundleOrAbort -InstallRoot $InstallRoot

    Install-IssMainService -InstallRoot $InstallRoot -ExpectedVersion $ExpectedVersion
    if ($script:InstallHealthStatus -ne 'install-health:ok') {
        throw "ISS mirror: AriaSignature service install verification failed: $($script:InstallHealthStatus)"
    }

    Install-IssMelezhService -InstallRoot $InstallRoot
}
