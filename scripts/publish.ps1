param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    & $Dotnet publish src/AirPodsSwitch.Diag -c Release -r win-x64 --self-contained true -o artifacts/diag
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic publish failed' }
    & $Dotnet publish src/AirPodsSwitch.Tray -c Release --self-contained true -o artifacts/tray
    if ($LASTEXITCODE -ne 0) { throw 'Tray publish failed' }
    foreach ($taskOutput in @('artifacts/diag','artifacts/tray')) {
        Copy-Item -LiteralPath LICENSE -Destination $taskOutput
        Copy-Item -LiteralPath docs/THIRD_PARTY.md -Destination $taskOutput
        Copy-Item -LiteralPath third_party -Destination $taskOutput -Recurse -Force
    }
    Write-Output 'Published Windows x64 programs under artifacts/. No drivers or system changes applied.'
} finally { Pop-Location }
