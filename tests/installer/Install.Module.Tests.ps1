BeforeAll {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
    Set-Location $repoRoot
    Import-Module (Join-Path $repoRoot "installer\lib\AriaSignature.Install.psm1") -Force
}

Describe "AriaSignature.Install module" {
    It "loads config with service names" {
        $cfg = Get-AriaInstallConfig
        $cfg.mainServiceName | Should -Be "AriaSignatureService"
        $cfg.melezhServiceName | Should -Be "AriaSignatureMelezhService"
    }

    It "validates melezh bundle when present" {
        $bundle = Join-Path $repoRoot "installer\melezh\bundle"
        if (-not (Test-Path (Join-Path $bundle "bin\melezh.bat"))) {
            Set-ItResult -Inconclusive -Because "OInt bundle not prepared; run prepare-melezh.ps1"
        }
        { Test-MelezhBundleComplete -MelezhRoot $bundle } | Should -Not -Throw
    }
}
