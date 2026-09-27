param(
    [string]$BuildDirectory = 'Builds/Windows-0.4-ui-validation',
    [string]$OutputParent = 'docs/verification/core04-phase9-player-ui',
    [ValidateRange(60, 600)][int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $BuildDirectory))
$playerPath = Join-Path $buildRoot 'TheWorstHotelEver.exe'
if (-not (Test-Path -LiteralPath $playerPath)) { throw 'Build the operations UI development player first.' }
$runName = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$capturePath = Join-Path ([IO.Path]::GetFullPath((Join-Path $projectRoot $OutputParent))) $runName
New-Item -ItemType Directory -Path $capturePath | Out-Null
$playerLog = Join-Path $capturePath 'player.log'
$playerArgs = @('-screen-width','1600','-screen-height','900','-screen-fullscreen','0',
    '-verifyHotel',"`"$capturePath`"",'-verifyOperationsUI','-hotelSolo','-logFile',"`"$playerLog`"")
$startedAt = [DateTime]::UtcNow
$deadline = $startedAt.AddSeconds($TimeoutSeconds)
$player = Start-Process -FilePath $playerPath -ArgumentList $playerArgs -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
while (-not $player.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) {
        Stop-Process -Id $player.Id -ErrorAction SilentlyContinue
        throw "Owned UI verification timed out. Inspect $playerLog"
    }
}
$player.Refresh()
if ($player.ExitCode -ne 0) { throw "UI verification failed (exit $($player.ExitCode)). Inspect $playerLog" }
$reportPath = Join-Path $capturePath 'runtime-verification.txt'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $startedAt) {
    throw 'No fresh operations UI report.'
}
$reportText = Get-Content -LiteralPath $reportPath -Raw
if ($reportText -notmatch 'Outcome=PASS Errors=0' -or $reportText -notmatch 'OperationsUIOnly=True OperationsUIVerified=True') {
    throw "Operations UI checks did not pass. Inspect $reportPath"
}
$manifestPath = Join-Path $capturePath 'capture-manifest.txt'
$manifest = @(Get-Content -LiteralPath $manifestPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($manifest.Count -ne 16) { throw 'Expected sixteen fresh player capture candidates.' }
foreach ($name in $manifest) {
    if ([IO.Path]::GetFileName($name) -ne $name) { throw 'Nonlocal capture name.' }
    $captureFile = Join-Path $capturePath $name
    if (-not (Test-Path -LiteralPath $captureFile) -or (Get-Item -LiteralPath $captureFile).LastWriteTimeUtc -lt $startedAt) {
        throw "Missing or stale capture: $name"
    }
}
$binaryFacts = @(('Build=' + $BuildDirectory),
    ('GameplayAssemblySHA256=' + (Get-FileHash -LiteralPath (Join-Path $buildRoot 'TheWorstHotelEver_Data/Managed/WorstHotel.Runtime.dll')).Hash),
    ('ExecutableSHA256=' + (Get-FileHash -LiteralPath $playerPath).Hash),
    'RuntimeChecks=PASS; ScreenshotLegibility=MANUAL_REVIEW_REQUIRED; ThreeDayLifecycle=False')
[IO.File]::WriteAllLines((Join-Path $capturePath 'binary-and-scope.txt'), $binaryFacts, [Text.UTF8Encoding]::new($false))
Write-Output ('CaptureFolder=' + $capturePath)
Write-Output $reportText
