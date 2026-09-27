param(
    [string]$OutputPath = 'docs/screenshots/runtime',
    [string]$BuildDirectory = 'Builds/Windows',
    [switch]$Solo,
    [switch]$PresenceFixtures,
    [switch]$AgencyFixtures,
    [switch]$ServiceFixtures,
    [switch]$ContinuousFixtures,
    [ValidateRange(60, 1800)][int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($ContinuousFixtures) {
    if (-not $Solo) { throw 'ContinuousFixtures requires -Solo for its exclusive single-staff lifecycle.' }
    if ($PresenceFixtures -or $AgencyFixtures -or $ServiceFixtures) { throw 'ContinuousFixtures cannot be combined with historical shift fixtures.' }
    if (-not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 1200 }
    if (-not $PSBoundParameters.ContainsKey('OutputPath')) {
        $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
        $OutputPath = 'docs/verification/core04-phase10-solo/' + $runId
    }
}
$playerPath = Join-Path (Join-Path $projectRoot $BuildDirectory) 'TheWorstHotelEver.exe'
if (-not (Test-Path -LiteralPath $playerPath)) { throw 'Build the Windows development player first.' }
$capturePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath))
New-Item -ItemType Directory -Path $capturePath -Force | Out-Null
$playerLog = if ($ContinuousFixtures) { Join-Path $capturePath 'player.log' } else { Join-Path $projectRoot 'Logs/player-verification.log' }
New-Item -ItemType Directory -Path (Split-Path -Parent $playerLog) -Force | Out-Null
$playerArgs = @('-screen-width','1600','-screen-height','900','-screen-fullscreen','0',
    '-verifyHotel',"`"$capturePath`"",'-logFile',"`"$playerLog`"")
if ($Solo) { $playerArgs += '-hotelSolo' }
if ($ContinuousFixtures) { $playerArgs += '-verifyHotelContinuous' }
if ($PresenceFixtures) {
    if (-not $Solo) { throw 'PresenceFixtures requires -Solo for its single-camera visual review.' }
    $playerArgs += '-verifyPresence'
}
if ($AgencyFixtures) {
    if (-not $Solo) { throw 'AgencyFixtures requires -Solo for real single-camera guest interaction.' }
    $playerArgs += '-verifyAgency'
    if (-not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 1140 }
}
if ($ServiceFixtures) {
    if (-not $Solo) { throw 'ServiceFixtures requires -Solo for physical service carrying and phone input.' }
    $playerArgs += '-verifyServices'
    if (-not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = if ($AgencyFixtures) { 1740 } else { 1200 } }
}
$startedAt = [DateTime]::UtcNow
$deadline = $startedAt.AddSeconds($TimeoutSeconds)
$player = Start-Process -FilePath $playerPath -ArgumentList $playerArgs -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
while (-not $player.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) {
        Stop-Process -Id $player.Id -ErrorAction SilentlyContinue
        throw "Living-hotel verification timed out after $TimeoutSeconds seconds. Inspect $playerLog"
    }
}
$player.Refresh()
if ($player.ExitCode -ne 0) { throw "Verification player failed (exit $($player.ExitCode)). Inspect $playerLog" }
$report = Join-Path $capturePath 'runtime-verification.txt'
if (-not (Test-Path -LiteralPath $report) -or (Get-Item -LiteralPath $report).LastWriteTimeUtc -lt $startedAt) {
    throw 'Player exited without a fresh verification report.'
}
$reportText = Get-Content -LiteralPath $report -Raw
Write-Output $reportText
if ($reportText -notmatch 'Outcome=PASS Errors=0 ResetVerified=True') { throw 'Living lifecycle or fresh-session reset did not pass.' }
if ($Solo -and ($reportText -notmatch 'Mode=Solo SyntheticPads=1' -or $reportText -notmatch 'SoloComposition=True StaffObjects=1 ActiveCameras=1 Listeners=1 SyntheticPads=1')) {
    throw 'SOLO did not verify one actual staff object, camera, listener and synthetic input device.'
}
if ($PresenceFixtures -and $reportText -notmatch 'GuestPresenceVerified=True') { throw 'Guest presence fixtures did not complete.' }
if ($AgencyFixtures -and $reportText -notmatch 'GuestAgencyVerified=True') { throw 'Guest agency fixtures did not complete.' }
if ($ServiceFixtures -and $reportText -notmatch 'GuestServicesVerified=True') { throw 'Guest service fixtures did not complete.' }
if ($ServiceFixtures -and $reportText -notmatch 'NaturalContactsVerified=True') { throw 'Natural guest contact fixtures did not complete.' }
if ($ContinuousFixtures) {
    foreach ($evidence in @('ContinuousOperationsVerified=True CashConserved=True ModelContinuity=True RoomRegistryContinuity=True ClockContinuity=True EpochContinuity=True',
        'Reports=3 UniquePaidStays=14 CurrentPeriodReceipts=5', 'StaffSlots=1 ModelKeyHandoffs=14', 'ContinuousFreshInventoryReset=True')) {
        if ($reportText -notmatch [regex]::Escape($evidence)) { throw "Continuous SOLO lacks $evidence. Inspect $report" }
    }
    foreach ($cohort in 1..3) {
        $count = if ($cohort -eq 1) { 4 } else { 5 }
        if ($reportText -notmatch "Cohort ${cohort}: booked=$count checkedIn=$count actualRoomArrivals=$count actualDepartures=$count paidStays=$count") {
            throw "Continuous cohort $cohort did not complete its actual routes and once-only payments."
        }
    }
    $snapshotPath = Join-Path $capturePath 'continuous-final-snapshot.json'
    if (-not (Test-Path -LiteralPath $snapshotPath) -or (Get-Item -LiteralPath $snapshotPath).LastWriteTimeUtc -lt $startedAt) {
        throw 'Continuous run did not preserve its original final state before NewGame.'
    }
}
if (-not $ContinuousFixtures) { foreach ($day in 1..3) {
    $expectedGuests = if ($day -eq 1) { 4 } else { 6 }
    if ($reportText -notmatch "Day ${day}: booked=$expectedGuests checkedIn=$expectedGuests actualRoomArrivals=$expectedGuests.*paidStays=$expectedGuests") {
        throw "Day $day did not record $expectedGuests actual served and paid stays."
    }
} }
$manifestPath = Join-Path $capturePath 'capture-manifest.txt'
if (-not (Test-Path -LiteralPath $manifestPath) -or (Get-Item -LiteralPath $manifestPath).LastWriteTimeUtc -lt $startedAt) {
    throw 'No fresh current-run capture manifest was written.'
}
$manifest = @(Get-Content -LiteralPath $manifestPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($manifest.Count -eq 0 -or @($manifest | Select-Object -Unique).Count -ne $manifest.Count) { throw 'Empty or duplicate capture manifest.' }
if ($ContinuousFixtures) {
    foreach ($required in @('continuous-opening', 'continuous-bookings', 'continuous-day1-guests', 'continuous-day2-preparation',
        'continuous-day2-guests', 'continuous-day3-preparation', 'continuous-day3-guests', 'continuous-72-hours',
        'continuous-reports', 'continuous-final-accounts', 'continuous-final-boiler', 'continuous-new-session')) {
        if ($manifest -notcontains ($required + '.png')) { throw "Continuous capture missing: $required" }
    }
}
if ($PresenceFixtures) {
    foreach ($requiredPresence in @('guest-sleep.png','guest-shower.png','guest-privacy.png','guest-rest.png','guest-debug.png')) {
        if ($manifest -notcontains $requiredPresence) { throw "Presence capture is missing: $requiredPresence" }
    }
}
if ($AgencyFixtures) {
    foreach ($requiredAgency in @('agency-noise-case.png','agency-conversation.png','agency-repeat.png','agency-credit.png','agency-relocation.png','agency-life.png','agency-cold.png','agency-away.png','agency-return.png')) {
        if ($manifest -notcontains $requiredAgency) { throw "Agency capture is missing: $requiredAgency" }
    }
}
$serviceCapture = if ($Solo) { 'solo-service.png' } else { 'split-screen.png' }
if ($ServiceFixtures) {
    foreach ($requiredService in @('service-board.png','service-blanket-stock.png','service-blanket-delivered.png','service-radiator.png',
        'service-lamp-off.png','service-lamp-on.png','service-electrical-consumers.png','service-wake-request.png','service-phone.png','service-late-checkout.png',
        'service-luggage-request.png','service-luggage-stored.png','natural-self-help.png','natural-phone-ringing.png',
        'natural-phone-heard.png','natural-reception-heard.png')) {
        if ($manifest -notcontains $requiredService) { throw "Service capture is missing: $requiredService" }
    }
}
if (-not $ContinuousFixtures) { foreach ($required in @('physical-sources.png', 'linen-source.png', 'planning.png', $serviceCapture, 'guest-detail.png', 'housekeeping-day2.png',
    'portable-heater.png', 'electrical-warning.png', 'electrical-tripped.png', 'maintenance.png', 'results.png', 'developer-panel.png', 'developer-controls.png', 'new-session.png')) {
    if ($manifest -notcontains $required) { throw "Current-run capture is missing: $required" }
} }
Add-Type -AssemblyName System.Drawing
$rejectedImages = @()
foreach ($captureName in $manifest) {
    # Ignore earlier screenshots that are not in this run's manifest; never count stale art as evidence.
    if ([IO.Path]::GetFileName($captureName) -ne $captureName) { throw 'Capture manifest contains a nonlocal file name.' }
    $imagePath = Join-Path $capturePath $captureName
    if (-not (Test-Path -LiteralPath $imagePath)) { throw "Missing captured file: $captureName" }
    if ((Get-Item -LiteralPath $imagePath).LastWriteTimeUtc -lt $startedAt) { throw "Stale captured file: $captureName" }
    $bitmap = [Drawing.Bitmap]::new($imagePath)
    try {
        $nonBlack = 0
        for ($iy = 1; $iy -lt 20; $iy++) {
            for ($ix = 1; $ix -lt 30; $ix++) {
                $pixel = $bitmap.GetPixel([int]($ix * $bitmap.Width / 30), [int]($iy * $bitmap.Height / 20))
                if ($pixel.R + $pixel.G + $pixel.B -gt 30) { $nonBlack++ }
            }
        }
        if ($nonBlack -lt 5) { $rejectedImages += $captureName }
    } finally { $bitmap.Dispose() }
}
if ($rejectedImages.Count -gt 0) {
    $limitation = 'VISUAL CAPTURE REJECTED (blank pixels): ' + ($rejectedImages -join ', ') + '. Lifecycle passed; rendered UI/performance is not verified by these files.'
    Add-Content -LiteralPath $report -Value $limitation
    throw $limitation
} else {
    Write-Output "Nonblank built-player image candidates: $capturePath. Inspect their layout before accepting."
}
if ($ContinuousFixtures) {
    $binaryFacts = @(
        ('Build=' + $BuildDirectory),
        ('GameplayAssemblySHA256=' + (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $playerPath) 'TheWorstHotelEver_Data/Managed/WorstHotel.Runtime.dll') -Algorithm SHA256).Hash),
        ('ExecutableSHA256=' + (Get-FileHash -LiteralPath $playerPath -Algorithm SHA256).Hash),
        'RuntimeScope=Continuous SOLO with explicitly labelled diagnostic staff actions; human controls, feel and performance require separate review.',
        'ScreenshotLegibility=MANUAL_REVIEW_REQUIRED'
    )
    [IO.File]::WriteAllLines((Join-Path $capturePath 'binary-and-scope.txt'), $binaryFacts, [Text.UTF8Encoding]::new($false))
    Write-Output "CaptureFolder=$capturePath"
}
