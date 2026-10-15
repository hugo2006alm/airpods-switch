param([string]$Dotnet = 'dotnet', [string]$OutputDirectory = 'artifacts/packages', [string]$ExpectedVersion = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    $taskDirty = git status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $taskDirty) { throw 'Packaging requires a clean Git checkout so the source archive matches the binaries.' }
    $taskCommit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read source revision' }
    $taskVersion = ([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
    if ($taskVersion -notmatch '^\d+\.\d+\.\d+-alpha\.\d+$') { throw 'Expected an explicit alpha version in Directory.Build.props' }
    if ($ExpectedVersion -and $ExpectedVersion -cne $taskVersion) { throw 'Release tag/version mismatch' }
    $taskDestination = [IO.Path]::GetFullPath($OutputDirectory, $taskRoot)
    New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
    $taskPrefix = "AirPodsSwitch-$taskVersion"
    $taskBinaryZip = Join-Path $taskDestination "$taskPrefix-win-x64.zip"
    $taskSourceZip = Join-Path $taskDestination "$taskPrefix-source.zip"
    $taskChecksums = Join-Path $taskDestination "$taskPrefix-SHA256SUMS.txt"
    foreach ($taskFile in @($taskBinaryZip, $taskSourceZip, $taskChecksums)) {
        if (Test-Path -LiteralPath $taskFile) { throw "Refusing to overwrite an existing package: $taskFile" }
    }
    # Each build gets an empty staging directory; stale DLLs from earlier publishes cannot enter the package.
    $taskStage = Join-Path $taskRoot ('artifacts/staging/' + [Guid]::NewGuid().ToString('N'))
    & "$PSScriptRoot/publish.ps1" -Dotnet $Dotnet -OutputRoot $taskStage
    @'
# AirPods Switch Windows alpha

Extract this whole ZIP. Launch `tray/AirPodsSwitch.Tray.exe` for guided setup.
The diagnostic CLI is `diag/airpods-diag.exe`. Read [the alpha guide](docs/ALPHA_GUIDE.md).
This is unsigned experimental software; actual AirPods switching remains unverified.
Keep the matching source ZIP and bundled license/notice files with distributed copies.
'@ | Set-Content -LiteralPath (Join-Path $taskStage 'START-HERE.md') -Encoding utf8
    Copy-Item -LiteralPath LICENSE -Destination $taskStage
    Copy-Item -LiteralPath docs/THIRD_PARTY.md -Destination $taskStage
    Copy-Item -LiteralPath docs -Destination $taskStage -Recurse
    @{
        version = $taskVersion; commit = $taskCommit.Trim(); architecture = 'win-x64'; channel = 'alpha'
        signed = $false; hardwareValidated = $false
        sourceArchive = [IO.Path]::GetFileName($taskSourceZip)
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskStage 'build-info.json') -Encoding utf8
    # Exercise published native dependency loading without showing or registering a notification.
    $taskReport = Join-Path $taskStage 'native-startup.json'
    $taskProcess = Start-Process -FilePath (Join-Path $taskStage 'tray/AirPodsSwitch.Tray.exe') -ArgumentList @('--self-test', ('"' + $taskReport + '"')) -WindowStyle Hidden -PassThru
    $null = $taskProcess.Handle
    if (-not $taskProcess.WaitForExit(30000)) { $taskProcess.Kill(); throw 'Published tray startup timed out' }
    if ($taskProcess.ExitCode -ne 0) { throw 'Published tray native startup failed' }
    if (-not (Test-Path -LiteralPath $taskReport)) { throw 'Native startup report missing' }
    & (Join-Path $taskStage 'diag/airpods-diag.exe') replay --file tests/fixtures/two-host.hex
    if ($LASTEXITCODE -ne 0) { throw 'Published CLI replay failed' }
    git archive --format=zip "--output=$taskSourceZip" HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed' }
    Compress-Archive -Path (Join-Path $taskStage '*') -DestinationPath $taskBinaryZip -CompressionLevel Optimal
    @($taskBinaryZip, $taskSourceZip) | ForEach-Object {
        $taskHash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        "$taskHash  $([IO.Path]::GetFileName($_))"
    } | Set-Content -LiteralPath $taskChecksums -Encoding utf8
    & $Dotnet restore AirPodsSwitch.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Canonical dependency lock validation failed' }
    if (git status --porcelain --untracked-files=normal) { throw 'Packaging changed tracked source/dependency files' }
    Write-Output "Versioned binary/source ZIPs and SHA-256 checksums: $taskDestination"
} finally { Pop-Location }
