<#
Starts a separate, diagnostic-owned SOLO player and an external observer.
The player and observer are hidden unless -Interactive is explicitly supplied.
Neither script terminates the game. This is evidence collection, not a test PASS.
#>
[CmdletBinding()]
param(
    [string]$BuildDirectory = 'Builds/Windows-0.3.2-pause-diagnostic',
    [string]$OutputPath = 'docs/verification/pause',
    [ValidateRange(30, 1800)][int]$ObserveSeconds = 1800,
    [ValidateRange(2, 5)][int]$SampleSeconds = 2,
    [switch]$Interactive
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$watchScript = Join-Path $PSScriptRoot 'Watch-PauseInvestigation.ps1'
. $watchScript -LibraryOnly
if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit PowerShell; no player was started.' }

$buildRoot = if ([IO.Path]::IsPathRooted($BuildDirectory)) { [IO.Path]::GetFullPath($BuildDirectory) }
    else { [IO.Path]::GetFullPath((Join-Path $projectRoot $BuildDirectory)) }
$playerPath = Join-Path $buildRoot 'TheWorstHotelEver.exe'
if (-not (Test-Path -LiteralPath $playerPath -PathType Leaf)) {
    throw "Diagnostic player not found: $playerPath. Build it first; no process was started."
}
$imagePath = [PauseInvestigationNative]::CanonicalFile($playerPath)
$binaryRecords = @(Get-PauseBinaryRecords -ImagePath $imagePath)
if ($binaryRecords.Count -ne 3) {
    throw 'The expected EXE, Mono gameplay DLL and UnityPlayer DLL are required for this hotel development build.'
}
$outputRoot = if ([IO.Path]::IsPathRooted($OutputPath)) { [IO.Path]::GetFullPath($OutputPath) }
    else { [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath)) }
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runPath = Join-Path $outputRoot $runId
New-Item -ItemType Directory -Path $runPath -ErrorAction Stop | Out-Null
$logPath = Join-Path $runPath 'Player.log'
$playerArguments = @('-hotelSolo', '-pauseDiagnostics', (ConvertTo-PauseArgument $runPath),
    '-logFile', (ConvertTo-PauseArgument $logPath))
$windowStyle = if ($Interactive) { 'Normal' } else { 'Hidden' }
$player = $null
try {
    $player = Start-Process -FilePath $imagePath -ArgumentList $playerArguments -WorkingDirectory $buildRoot `
        -WindowStyle $windowStyle -PassThru
    if ($player.Id -eq 32284) { throw 'Protected PID 32284 is never observed by this workflow.' }
    $identity = [PauseInvestigationNative]::ReadIdentity($player.Id)
    if (-not [string]::Equals($imagePath, $identity.ImagePath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The started process image does not match the explicitly selected build.'
    }
    $manifest = [ordered]@{
        SchemaVersion = 1; RunId = $runId; CreatedUtc = [DateTime]::UtcNow.ToString('o')
        ProcessId = $player.Id; StartUtc = $identity.StartUtc; StartFileTimeUtc = $identity.StartFileTimeUtc
        ImagePath = $imagePath; BinaryFiles = $binaryRecords; PlayerArguments = $playerArguments
        PlayerLog = $logPath; HeartbeatPath = (Join-Path $runPath 'heartbeat.json')
        Interactive = [bool]$Interactive; ObserverSeconds = $ObserveSeconds; SampleSeconds = $SampleSeconds
        SuspectAfterSeconds = 20; StartupHeartbeatGraceSeconds = 60; MaximumDumps = 2; DumpSpacingSeconds = 10
        DumpFlags = '0x1924'; ProtectedProcessIds = @(32284)
        Interpretation = 'Evidence collection only. Missing heartbeat is a suspect stall, not a root cause or a test failure.'
    }
    Write-PauseJson -Path (Join-Path $runPath 'launch.json') -Value $manifest
    $shellPath = Get-PauseShell
    $watchArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (ConvertTo-PauseArgument $watchScript),
        '-RunDirectory', (ConvertTo-PauseArgument $runPath))
    $observer = Start-Process -FilePath $shellPath -ArgumentList $watchArguments -WorkingDirectory $projectRoot `
        -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runPath 'observer.stdout.txt') `
        -RedirectStandardError (Join-Path $runPath 'observer.stderr.txt') -PassThru
    $result = [ordered]@{
        RunId = $runId; PlayerProcessId = $player.Id; ObserverProcessId = $observer.Id
        PlayerStartUtc = $identity.StartUtc; RunDirectory = $runPath; Interactive = [bool]$Interactive
        ObserverLimitSeconds = $ObserveSeconds
        Status = 'Started; no reproduction or stability result yet. Closing the observer never closes the player.'
    }
    Write-PauseJson -Path (Join-Path $runPath 'started.json') -Value $result
    [pscustomobject]$result
} catch {
    $failure = [ordered]@{ Utc = [DateTime]::UtcNow.ToString('o'); Error = $_.Exception.Message
        StartedPlayerProcessId = $(if ($null -ne $player) { $player.Id } else { $null })
        PlayerWasTerminated = $false; RunDirectory = $runPath }
    Write-PauseJson -Path (Join-Path $runPath 'launcher-error.json') -Value $failure
    if ($null -ne $player) { Write-Warning "Player PID $($player.Id) was left running; launcher failed. Evidence: $runPath" }
    throw
} finally {
    if ($null -ne $player) { $player.Dispose() }
}
