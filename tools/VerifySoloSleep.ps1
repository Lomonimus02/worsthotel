param(
    [string]$BuildDirectory = 'Builds/Windows-0.4.1',
    [string]$OutputPath,
    [ValidateRange(120, 900)][int]$TimeoutSeconds = 480
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $OutputPath = 'docs/verification/rhythm041-solo-sleep/' + $runId
}
$buildPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $BuildDirectory))
$playerPath = Join-Path $buildPath 'TheWorstHotelEver.exe'
$assemblyPath = Join-Path $buildPath 'TheWorstHotelEver_Data/Managed/WorstHotel.Runtime.dll'
foreach ($required in @($playerPath, $assemblyPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Build the development player first: $required" }
}
$capturePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath))
New-Item -ItemType Directory -Path $capturePath -Force | Out-Null
$playerLog = Join-Path $capturePath 'player.log'
$initialExeHash = (Get-FileHash -LiteralPath $playerPath -Algorithm SHA256).Hash
$initialAssemblyHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
$arguments = @('-screen-width', '1600', '-screen-height', '900', '-screen-fullscreen', '0',
    '-hotelSolo', '-verifySoloSleep', '-verifyHotel', "`"$capturePath`"", '-logFile', "`"$playerLog`"")
$startedAt = [DateTime]::UtcNow
$deadline = $startedAt.AddSeconds($TimeoutSeconds)
$verificationProcess = Start-Process -FilePath $playerPath -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
$ownedStart = $verificationProcess.StartTime.ToUniversalTime().ToFileTimeUtc()
while (-not $verificationProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -le $deadline) { continue }
    # Terminate only this wrapper's exact still-running process after its own bounded diagnostic.
    # Never select a process by name, discover another game, or touch a user's older game window.
    $verificationProcess.Refresh()
    if (-not $verificationProcess.HasExited) {
        if ($verificationProcess.StartTime.ToUniversalTime().ToFileTimeUtc() -ne $ownedStart -or
            -not [string]::Equals([IO.Path]::GetFullPath($verificationProcess.MainModule.FileName), $playerPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Timed-out diagnostic identity changed; no process was stopped.'
        }
        $verificationProcess.Kill()
        $verificationProcess.WaitForExit(5000) | Out-Null
    }
    throw "SOLO sleep diagnostic exceeded $TimeoutSeconds seconds. Inspect $playerLog"
}
$verificationProcess.Refresh()
if ($verificationProcess.ExitCode -ne 0) { throw "SOLO sleep diagnostic failed (exit $($verificationProcess.ExitCode)); inspect $playerLog" }
if ((Get-FileHash -LiteralPath $playerPath -Algorithm SHA256).Hash -ne $initialExeHash -or
    (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash -ne $initialAssemblyHash) {
    throw 'The executable or gameplay assembly changed during verification.'
}
$reportPath = Join-Path $capturePath 'runtime-verification.txt'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $startedAt) {
    throw 'The player did not produce a fresh SOLO sleep report.'
}
$report = Get-Content -LiteralPath $reportPath -Raw
Write-Output $report
foreach ($proof in @(
    'Outcome=PASS Errors=0 ResetVerified=True',
    'Mode=Solo SyntheticPads=1',
    'SoloComposition=True StaffObjects=1 ActiveCameras=1 Listeners=1 SyntheticPads=1',
    'SoloSleepVerified=True ActualBedInput=True DriverClockOverride=False',
    'FreshCancelVerified=True MorningWakeVerified=True CriticalCircuitWakeVerified=True ModelWorldContinuity=True ModelOnlyAcceleration=True',
    'ReportsAdded=1', 'WarningObservedWhileAsleep=True', 'ContinuousFreshInventoryReset=True')) {
    if ($report -notmatch [regex]::Escape($proof)) { throw "SOLO sleep report lacks required evidence: $proof" }
}
$manifestPath = Join-Path $capturePath 'capture-manifest.txt'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or (Get-Item -LiteralPath $manifestPath).LastWriteTimeUtc -lt $startedAt) {
    throw 'A fresh GPU capture manifest is required.'
}
$manifest = @(Get-Content -LiteralPath $manifestPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($manifest.Count -eq 0 -or @($manifest | Select-Object -Unique).Count -ne $manifest.Count) { throw 'Empty or duplicate image manifest.' }
foreach ($capture in @('solo-sleep-staff-room.png', 'solo-sleep-active.png', 'solo-sleep-morning.png',
    'solo-sleep-critical-wake.png', 'solo-sleep-new-session.png')) {
    if ($manifest -notcontains $capture) { throw "Missing required current-run image: $capture" }
}
Add-Type -AssemblyName System.Drawing
$imageFacts = @()
foreach ($name in $manifest) {
    if ([IO.Path]::GetFileName($name) -ne $name) { throw 'Capture manifest contains a nonlocal path.' }
    $path = Join-Path $capturePath $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $startedAt) {
        throw "Missing or stale capture: $name"
    }
    $bitmap = [Drawing.Bitmap]::new($path)
    try {
        $nonBlack = 0
        for ($iy = 1; $iy -lt 20; $iy++) {
            for ($ix = 1; $ix -lt 30; $ix++) {
                $pixel = $bitmap.GetPixel([int]($ix * $bitmap.Width / 30), [int]($iy * $bitmap.Height / 20))
                if ($pixel.R + $pixel.G + $pixel.B -gt 30) { $nonBlack++ }
            }
        }
        if ($nonBlack -lt 5) { throw "Blank capture rejected: $name. This image is not rendered evidence." }
        $imageFacts += "$name $($bitmap.Width)x$($bitmap.Height) SHA256=$((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)"
    }
    finally { $bitmap.Dispose() }
}
$binaryFacts = @(
    "Build=$buildPath",
    "ProcessId=$($verificationProcess.Id) StartUtc=$($startedAt.ToString('o'))",
    "ExecutableSHA256=$initialExeHash",
    "GameplayAssemblySHA256=$initialAssemblyHash",
    'Scope=Actual SOLO bed consent/cancel/morning plus actual consumer-caused circuit wake, in two isolated production sessions.',
    'Adapters=Normal pre-sleep calendar advance and sales policies; model key handoff after real reception; uncarried off-heater placement; empty-staff approaches.',
    'NotClaimed=Heater carrying, room104-to-utility walking, three-day economy, human pacing, performance, long-pause or AltTab repair.',
    'ScreenshotLegibility=MANUAL_REVIEW_REQUIRED'
)
[IO.File]::WriteAllLines((Join-Path $capturePath 'binary-and-scope.txt'), $binaryFacts, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines((Join-Path $capturePath 'image-hashes.txt'), $imageFacts, [Text.UTF8Encoding]::new($false))
Write-Output "CaptureFolder=$capturePath"
Write-Output 'SOLO sleep mechanics passed; all image layouts still require visual review.'
