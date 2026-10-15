param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    & $Dotnet build AirPodsSwitch.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    if (Test-Path 'tests/AirPodsSwitch.Tests') {
        & $Dotnet test tests/AirPodsSwitch.Tests -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    }
} finally { Pop-Location }
