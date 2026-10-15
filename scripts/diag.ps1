# Use a project-local SDK when present, otherwise the installed .NET SDK.
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
& $taskDotnet run --project (Join-Path $taskRoot 'src/AirPodsSwitch.Diag') -c Release --no-launch-profile -- @args
exit $LASTEXITCODE
