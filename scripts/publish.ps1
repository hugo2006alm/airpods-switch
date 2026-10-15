param([string]$Dotnet = 'dotnet', [string]$OutputRoot = 'artifacts')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    $taskOutputRoot = [IO.Path]::GetFullPath($OutputRoot, $taskRoot)
    & $Dotnet publish src/AirPodsSwitch.Diag -c Release -r win-x64 --self-contained true -o (Join-Path $taskOutputRoot 'diag')
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic publish failed' }
    & $Dotnet publish src/AirPodsSwitch.Tray -c Release --self-contained true -o (Join-Path $taskOutputRoot 'tray')
    if ($LASTEXITCODE -ne 0) { throw 'Tray publish failed' }
    $taskNugetRoot = & $Dotnet msbuild src/AirPodsSwitch.Diag/AirPodsSwitch.Diag.csproj -nologo -getProperty:NuGetPackageRoot
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate runtime notices' }
    foreach ($taskOutput in @((Join-Path $taskOutputRoot 'diag'), (Join-Path $taskOutputRoot 'tray'))) {
        Copy-Item -LiteralPath LICENSE -Destination $taskOutput
        Copy-Item -LiteralPath docs/THIRD_PARTY.md -Destination $taskOutput
        Copy-Item -LiteralPath third_party -Destination $taskOutput -Recurse -Force
        $taskRuntimeConfig = Get-ChildItem -LiteralPath $taskOutput -Filter '*.runtimeconfig.json' | Select-Object -First 1
        foreach ($taskFramework in (Get-Content -LiteralPath $taskRuntimeConfig.FullName -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks) {
            $taskRuntimePackage = switch ($taskFramework.name) {
                'Microsoft.NETCore.App' { 'microsoft.netcore.app.runtime.win-x64' }
                'Microsoft.WindowsDesktop.App' { 'microsoft.windowsdesktop.app.runtime.win-x64' }
                default { throw 'Unknown bundled framework; review redistribution notices' }
            }
            $taskNoticeTarget = Join-Path $taskOutput "third_party/$taskRuntimePackage"
            New-Item -ItemType Directory -Path $taskNoticeTarget -Force | Out-Null
            $taskPackagePath = Join-Path $taskNugetRoot.Trim() "$taskRuntimePackage/$($taskFramework.version)"
            $taskNotices = @(Get-ChildItem -LiteralPath $taskPackagePath -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\..*)?$' })
            if ($taskNotices.Count -eq 0) { throw "Runtime notices missing: $taskRuntimePackage" }
            foreach ($taskNotice in $taskNotices) { Copy-Item -LiteralPath $taskNotice.FullName -Destination $taskNoticeTarget }
        }
    }
    Write-Output "Published Windows x64 programs under $taskOutputRoot. No drivers or system changes applied."
} finally {
    # RID-specific publishing temporarily rewrites NuGet lock targets. Restore the
    # solution's normal graph so publishing does not leave tracked lockfiles dirty.
    try {
        & $Dotnet restore AirPodsSwitch.slnx
        if ($LASTEXITCODE -ne 0) { throw 'Canonical solution restore failed after publish' }
    } finally { Pop-Location }
}
