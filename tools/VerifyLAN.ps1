param(
    [string]$OutputPath = 'docs/verification/lan',
    [string]$BuildDirectory = 'Builds/Windows',
    [ValidateRange(60, 600)][int]$TimeoutSeconds = 150,
    [ValidateRange(1024, 65535)][int]$Port = 17777,
    [switch]$Capture,
    [switch]$AgencyFixtures,
    [switch]$ServiceFixtures,
    [switch]$ContinuousFixtures,
    [switch]$SleepFixtures
)
$ErrorActionPreference = 'Stop'
if ((@($AgencyFixtures, $ServiceFixtures, $ContinuousFixtures, $SleepFixtures) | Where-Object { $_ }).Count -gt 1) { throw 'Choose only one fixture mode per LAN run.' }
if ($ServiceFixtures -and -not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 345 }
if ($ContinuousFixtures -and -not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 480 }
if ($SleepFixtures -and -not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 480 }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$playerPath = Join-Path (Join-Path $projectRoot $BuildDirectory) 'TheWorstHotelEver.exe'
if (-not (Test-Path -LiteralPath $playerPath)) { throw 'Build the Windows development player with the LAN verification driver first.' }
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath))
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runPath = Join-Path $outputRoot $runId
New-Item -ItemType Directory -Path $runPath -Force | Out-Null
$startedAt = [DateTime]::UtcNow
$deadline = $startedAt.AddSeconds($TimeoutSeconds)
$ownedPlayers = [Collections.Generic.List[Diagnostics.Process]]::new()

function Start-OwnedHotel([string]$Side) {
    $log = Join-Path $runPath ($Side + '-player.log')
    $captureView = $Capture -and $Side -eq 'client'
    $windowWidth = if ($captureView) { 1600 } else { 960 }
    $windowHeight = if ($captureView) { 900 } else { 540 }
    $arguments = @('-screen-width', "$windowWidth", '-screen-height', "$windowHeight", '-screen-fullscreen', '0',
        '-verifyHotelLAN', "`"$runPath`"", '-hotelPort', "$Port", '-logFile', "`"$log`"")
    if ($Side -eq 'host') { $arguments += '-hotelHost' }
    else { $arguments += @('-hotelJoin', '127.0.0.1') }
    if ($Capture -and $Side -eq 'client') { $arguments += '-verifyLanCapture' }
    if ($AgencyFixtures) { $arguments += '-verifyLanAgency' }
    if ($ServiceFixtures) { $arguments += '-verifyLanServices' }
    if ($ContinuousFixtures) { $arguments += '-verifyLanContinuous' }
    if ($SleepFixtures) { $arguments += '-verifyLanSleep' }
    $process = Start-Process -FilePath $playerPath -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
    $ownedPlayers.Add($process)
    return $process
}

try {
    $hostPlayer = Start-OwnedHotel 'host'
    $hostReady = Join-Path $runPath 'host-listening.stage'
    while (-not (Test-Path -LiteralPath $hostReady)) {
        $hostPlayer.Refresh()
        if ($hostPlayer.HasExited) { throw "Host exited before listening (exit $($hostPlayer.ExitCode)). Inspect $runPath" }
        if ([DateTime]::UtcNow -gt $deadline) { throw "LAN verification timed out waiting for host. Inspect $runPath" }
        Start-Sleep -Milliseconds 250
    }
    $clientPlayer = Start-OwnedHotel 'client'
    while ($true) {
        $hostPlayer.Refresh(); $clientPlayer.Refresh()
        if ($hostPlayer.HasExited -and $hostPlayer.ExitCode -ne 0) { throw "Host LAN verification failed (exit $($hostPlayer.ExitCode)). Inspect $runPath" }
        if ($clientPlayer.HasExited -and $clientPlayer.ExitCode -ne 0) { throw "Client LAN verification failed (exit $($clientPlayer.ExitCode)). Inspect $runPath" }
        if ($hostPlayer.HasExited -and $clientPlayer.HasExited) { break }
        if ([DateTime]::UtcNow -gt $deadline) { throw "LAN verification exceeded $TimeoutSeconds seconds. Inspect $runPath" }
        Start-Sleep -Milliseconds 500
    }
    foreach ($side in @('host', 'client')) {
        $reportPath = Join-Path $runPath ($side + '-report.txt')
        if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $startedAt) {
            throw "No fresh $side report. Inspect $runPath"
        }
        $report = Get-Content -LiteralPath $reportPath -Raw
        Write-Output $report
        if ($report -notmatch "Outcome=PASS Errors=0 Role=$side" -or $report -notmatch "Run=$runId") {
            throw "$side did not pass with a fresh zero-error report. Inspect $runPath"
        }
        if ($SleepFixtures) {
            foreach ($evidence in @('Mode=SleepFixtures', 'ProductionContinuous=True', 'OwnedLocalPads=True',
                'HostFirstConsent=True', 'RemoteFirstConsent=True', 'DistinctBeds=True', 'ReleasePreservesConsent=True',
                'FreshUseCancels=True', 'HostFreshUseCancels=True', 'LeaseExpiryClearsSleep=True', 'LeaseRenewalDoesNotResume=True',
                'DisconnectClearsSleep=True', 'RejoinRequiresFreshConsent=True', 'HostHotelPreservedOnRejoin=True',
                'NewGameClearsSleep=True', 'OldEpochInputRejected=True', 'CurrentEpochInputWorks=True',
                'CriticalWarningKeptSleep=True', 'ActualHeaterLoadTrip=True', 'CriticalWakeMirrored=True', 'TripWakeNormalSpeed=True',
                'MorningWake=True', 'SnapshotSleepAuthority=True', 'SleepFixturesVerified=True')) {
                if ($report -notmatch [regex]::Escape($evidence)) { throw "$side lacks sleep evidence $evidence. Inspect $runPath" }
            }
            if ($side -eq 'client' -and ($report -notmatch 'ReadOnlyMirror=True ReadOnlyTickRejected=True SnapshotOnlyClockChecks=' -or
                $report -notmatch 'DisconnectedReadOnly=True')) {
                throw 'Client sleep report lacks snapshot-only clock or disconnected read-only evidence.'
            }
        }
        elseif ($ContinuousFixtures) {
            foreach ($evidence in @('Mode=ContinuousFixtures', 'PhysicalLedgerAccess=True', 'SalesPolicyRoundtrip=True',
                'AutomaticBooking=True', 'Reassignment=True', 'AgreedPricePreserved=True', 'StaleRevisionRejected=True', 'StalePolicyRejected=True',
                'Cancellation=True', 'MidnightPersistence=True', 'ReportOnce=True', 'PostBoundaryReassignment=True',
                'RemotePaidUpgrades=True', 'UpgradeDoesNotRepair=True',
                'DuplicateCapitalRejected=True', 'OtherBranchRejected=True', 'RemotePaidMaintenance=True',
                'MaintenanceDowntime=True', 'DuplicateMaintenanceRejected=True', 'MaintenanceRestored=True',
                'ThreeAccountingBoundaries=True', 'ContinuousCapitalVerified=True')) {
                if ($report -notmatch [regex]::Escape($evidence)) { throw "$side lacks continuous evidence $evidence. Inspect $runPath" }
            }
            if ($side -eq 'client' -and $report -notmatch 'ReadOnlyMirror=True SnapshotOnlyClockChecks=') {
                throw 'Client continuous report lacks snapshot-only authority evidence.'
            }
        }
        elseif ($ServiceFixtures) {
            foreach ($evidence in @('Mode=ServiceFixtures', 'PhysicalBlanketPickup=True', 'PhysicalBlanketDelivery=True',
                'ServiceStateReplicated=True', 'CrossActorOwnershipRejected=True', 'UngrantedCallRejected=True',
                'RemoteRadiator=True', 'RemotePhonePromise=True')) {
                if ($report -notmatch [regex]::Escape($evidence)) { throw "$side lacks service evidence $evidence. Inspect $runPath" }
            }
        }
        elseif ($AgencyFixtures) {
            if ($report -notmatch 'Mode=AgencyFixtures' -or $report -notmatch 'RemoteGuestContext=True' -or
                $report -notmatch 'ActualNoiseReduced=True' -or $report -notmatch 'CausalMemoryReplicated=True') {
                throw "$side lacks real remote guest-context, source-change or causal snapshot evidence. Inspect $runPath"
            }
            if ($side -eq 'client' -and ($report -notmatch 'ContextCancelReopen=True' -or $report -notmatch 'RemotePermissionEntry=True')) {
                throw 'Client agency report lacks repeated physical conversation or permission-entry evidence.'
            }
        }
        elseif ($side -eq 'host' -and ($report -notmatch 'PhysicalPickup=True' -or $report -notmatch 'DisconnectRelease=True')) {
            throw 'Host report lacks real remote physical pickup or disconnect-release evidence.'
        }
        if (-not $SleepFixtures -and -not $ContinuousFixtures -and -not $AgencyFixtures -and -not $ServiceFixtures -and $side -eq 'client' -and ($report -notmatch 'WorldPoseAgreement=True' -or $report -notmatch 'AssignRoundtrip=True CommitRoundtrip=True' -or
            $report -notmatch 'DisconnectedReadOnly=True')) { throw 'Client report lacks model roundtrip, pose agreement or read-only disconnect evidence.' }
    }
    if ($Capture) {
        $captureNames = if ($SleepFixtures) { @('client-sleep-staff-room.png', 'client-sleep-host-ready.png',
            'client-sleep-both-ready.png', 'client-sleep-wake-cancelled.png', 'client-sleep-disconnected.png',
            'client-sleep-rejoined.png', 'client-sleep-fresh-epoch.png', 'client-sleep-morning.png', 'client-sleep-critical-wake.png') }
            elseif ($ContinuousFixtures) { @('client-continuous-sales.png', 'client-continuous-bookings.png', 'client-continuous-boundary.png', 'client-continuous-report.png',
            'client-continuous-capital-before.png', 'client-continuous-capital-installed.png', 'client-continuous-maintenance-active.png',
            'client-continuous-maintenance-complete.png', 'client-continuous-report-2.png', 'client-continuous-report-3.png') }
            elseif ($ServiceFixtures) { @('client-service-stock.png', 'client-service-delivered.png', 'client-service-phone.png',
            'client-natural-cold-ringing.png', 'client-natural-cold-heard.png', 'client-natural-wake-heard.png') }
            elseif ($AgencyFixtures) { @('client-agency-context.png', 'client-agency-quiet.png') }
            else { @('client-connection-menu.png', 'client-one-camera-play.png', 'client-host-join-menu.png') }
        foreach ($name in $captureNames) {
            $capturePath = Join-Path $runPath $name
            if (-not (Test-Path -LiteralPath $capturePath) -or (Get-Item -LiteralPath $capturePath).LastWriteTimeUtc -lt $startedAt) {
                throw "No fresh requested GPU capture: $name"
            }
        }
        Write-Output 'Fresh GPU candidates captured with native IMGUI. Inspect pixels and layout separately.'
    }
    $scenario = if ($SleepFixtures) { 'physical two-staff sleep, wake and reconnect' } elseif ($ContinuousFixtures) { 'continuous bookings and reporting' } elseif ($ServiceFixtures) { 'physical guest services' } elseif ($AgencyFixtures) { 'guest-agency interaction' } else { 'physical-key smoke' }
    Write-Output "LAN localhost $scenario passed in two actual EXE processes. Reports: $runPath"
    Write-Output 'This verifies the local transport path, not a second computer, firewall configuration, human controls, graphics or performance.'
    if ($ContinuousFixtures -or $SleepFixtures) {
        $binaryFacts = @(
            ('Build=' + $BuildDirectory),
            ('GameplayAssemblySHA256=' + (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $playerPath) 'TheWorstHotelEver_Data/Managed/WorstHotel.Runtime.dll') -Algorithm SHA256).Hash),
            ('ExecutableSHA256=' + (Get-FileHash -LiteralPath $playerPath -Algorithm SHA256).Hash),
            $(if ($SleepFixtures) { 'RuntimeScope=Two actual localhost EXE processes with distinct owned local pads; closed room-sales policies, labelled empty-staff approaches and evening clock setup; separate actual guest routes with model-key/body-placement/switch/reset critical-load adapters; ordinary electrical warning/trip wake, normal sleep until 06:00 after NewGame, actual disconnect/rejoin and constructed old-epoch negative input on the normal NGO channel.' }
              else { 'RuntimeScope=Two actual localhost EXE processes; funds-only capital fixture and labelled host clock advances through three accounting boundaries.' }),
            'ScreenshotLegibility=MANUAL_REVIEW_REQUIRED; ThreeNaturalGuestCohorts=False; SecondComputer=False'
        )
        [IO.File]::WriteAllLines((Join-Path $runPath 'binary-and-scope.txt'), $binaryFacts, [Text.UTF8Encoding]::new($false))
    }
} finally {
    # These Process objects came only from this script's Start-Process calls. Never enumerate/stop Unity or other hotel runs.
    foreach ($player in $ownedPlayers) {
        $player.Refresh()
        if (-not $player.HasExited) { Stop-Process -Id $player.Id -ErrorAction SilentlyContinue; $player.WaitForExit(5000) | Out-Null }
        $player.Dispose()
    }
}
