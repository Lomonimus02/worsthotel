<#
External observer for a process created by Start-PauseInvestigation.ps1.
Only launch.json identifies the target; no process-name search or termination exists.
Capture mode is an internal, separate process so a blocked dump writer does not block sampling.
#>
[CmdletBinding()]
param(
    [string]$RunDirectory,
    [ValidateSet('Observe', 'Capture')][string]$Mode = 'Observe',
    [ValidateRange(0, 2)][int]$CaptureIndex = 0,
    [ValidateSet('HeartbeatStall', 'ToolSmoke', 'UserReportedHang')][string]$CaptureReason = 'HeartbeatStall',
    [switch]$LibraryOnly
)
$ErrorActionPreference = 'Stop'

if (-not ('PauseInvestigationNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class PauseProcessIdentity {
    public int ProcessId;
    public string StartUtc;
    public string StartFileTimeUtc;
    public string ImagePath;
}
public static class PauseInvestigationNative {
    const uint QueryLimitedInformation = 0x1000;
    const uint DumpAccess = 0x0450; // QUERY_INFORMATION | VM_READ | DUP_HANDLE
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetFinalPathNameByHandle(IntPtr file, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    // Absolute system-library path plus the prior signature check prevents DLL search-path substitution.
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true, SetLastError=true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    delegate bool DumpWriter(IntPtr process, uint pid, IntPtr file, uint kind, IntPtr exception, IntPtr streams, IntPtr callback);
    static void Check(bool okay) { if (!okay) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    public static string CanonicalFile(string path) {
        IntPtr handle = CreateFile(Path.GetFullPath(path), 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var result = new StringBuilder(32768);
            uint count = GetFinalPathNameByHandle(handle, result, (uint)result.Capacity, 0);
            Check(count != 0);
            if (count >= result.Capacity) throw new IOException("Resolved path exceeds buffer.");
            string value = result.ToString();
            if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + value.Substring(8);
            return value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value.Substring(4) : value;
        } finally { CloseHandle(handle); }
    }
    static PauseProcessIdentity IdentityOnHandle(IntPtr handle, int pid) {
        long created, exited, kernel, user;
        Check(GetProcessTimes(handle, out created, out exited, out kernel, out user));
        var path = new StringBuilder(32768); int size = path.Capacity;
        Check(QueryFullProcessImageName(handle, 0, path, ref size));
        return new PauseProcessIdentity { ProcessId = pid, StartFileTimeUtc = created.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartUtc = DateTime.FromFileTimeUtc(created).ToString("o"), ImagePath = CanonicalFile(path.ToString()) };
    }
    static void GuardPid(int pid) { if (pid <= 0 || pid == 32284) throw new InvalidOperationException("Invalid or explicitly protected process ID."); }
    public static PauseProcessIdentity ReadIdentity(int pid) {
        GuardPid(pid);
        IntPtr handle = OpenProcess(QueryLimitedInformation, false, pid);
        Check(handle != IntPtr.Zero);
        try { return IdentityOnHandle(handle, pid); } finally { CloseHandle(handle); }
    }
    public static void WriteDump(int pid, string expectedCreated, string expectedImage, string dumpPath, string systemDbgHelp) {
        GuardPid(pid);
        IntPtr handle = OpenProcess(DumpAccess, false, pid);
        Check(handle != IntPtr.Zero);
        IntPtr library = IntPtr.Zero;
        try {
            var identity = IdentityOnHandle(handle, pid);
            if (identity.StartFileTimeUtc != expectedCreated || !String.Equals(identity.ImagePath, expectedImage, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Identity changed; dump refused.");
            string expectedLibrary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dbghelp.dll");
            if (!String.Equals(Path.GetFullPath(systemDbgHelp), expectedLibrary, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only system DbgHelp is permitted.");
            // LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32
            library = LoadLibraryEx(systemDbgHelp, IntPtr.Zero, 0x00000900);
            Check(library != IntPtr.Zero);
            IntPtr export = GetProcAddress(library, "MiniDumpWriteDump"); Check(export != IntPtr.Zero);
            var writer = (DumpWriter)Marshal.GetDelegateForFunctionPointer(export, typeof(DumpWriter));
            using (var file = new FileStream(dumpPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
                Check(writer(handle, (uint)pid, file.SafeFileHandle.DangerousGetHandle(), 0x1924,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
                file.Flush(true);
            }
        } finally { if (library != IntPtr.Zero) FreeLibrary(library); CloseHandle(handle); }
    }
}
'@
}

function ConvertTo-PauseArgument([string]$Value) {
    if ($Value.Contains('"') -or $Value.Contains("`r") -or $Value.Contains("`n") -or $Value.EndsWith('\')) {
        throw 'Unexpected quote, line break or trailing slash in a process argument.'
    }
    return '"' + $Value + '"'
}
function Get-PauseShell {
    if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit PowerShell for this 64-bit player.' }
    $shell = Join-Path $PSHOME 'pwsh.exe'
    if (-not (Test-Path -LiteralPath $shell)) { $shell = Join-Path $PSHOME 'powershell.exe' }
    if (-not (Test-Path -LiteralPath $shell -PathType Leaf)) { throw 'Current PowerShell executable was not found.' }
    return $shell
}
function Write-PauseJson([string]$Path, $Value) {
    $text = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}
function Add-PauseRecord([string]$Path, $Value) {
    $text = ($Value | ConvertTo-Json -Depth 10 -Compress) + [Environment]::NewLine
    [IO.File]::AppendAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}
function Open-PauseSharedRead([string]$Path) {
    # The Unity recorder atomically replaces heartbeat.json. Readers must allow deletion/replacement.
    return [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
}
function Read-PauseSharedText([string]$Path) {
    $stream = Open-PauseSharedRead $Path
    $reader = $null
    try { $reader = [IO.StreamReader]::new($stream); return $reader.ReadToEnd() }
    finally { if ($null -ne $reader) { $reader.Dispose() } else { $stream.Dispose() } }
}
function Copy-PauseSharedFile([string]$Source, [string]$Destination) {
    $inputStream = Open-PauseSharedRead $Source
    $outputStream = $null
    try {
        $outputStream = [IO.FileStream]::new($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $inputStream.CopyTo($outputStream)
    } finally { if ($null -ne $outputStream) { $outputStream.Dispose() }; $inputStream.Dispose() }
}
function Get-PauseSharedHash([string]$Path) {
    $stream = Open-PauseSharedRead $Path
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
function Get-PauseBinaryRecords([string]$ImagePath) {
    $directory = [IO.Path]::GetDirectoryName($ImagePath)
    $candidates = @(
        @{ Role = 'Launcher'; Path = $ImagePath },
        @{ Role = 'Gameplay'; Path = (Join-Path $directory 'TheWorstHotelEver_Data/Managed/WorstHotel.Runtime.dll') },
        @{ Role = 'UnityPlayer'; Path = (Join-Path $directory 'UnityPlayer.dll') }
    )
    foreach ($entry in $candidates) {
        if (Test-Path -LiteralPath $entry.Path -PathType Leaf) {
            $file = Get-Item -LiteralPath $entry.Path
            [pscustomobject]@{ Role = $entry.Role; Path = [PauseInvestigationNative]::CanonicalFile($entry.Path)
                Length = $file.Length; LastWriteUtc = $file.LastWriteTimeUtc.ToString('o')
                SHA256 = (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash }
        }
    }
}
function Assert-PauseIdentity($Manifest) {
    $identity = [PauseInvestigationNative]::ReadIdentity([int]$Manifest.ProcessId)
    # PowerShell 7 may deserialize ISO dates into DateTime; compare the exact UTC instant, not culture-formatted strings.
    $recordedUtcFileTime = ([datetime]$Manifest.StartUtc).ToUniversalTime().ToFileTimeUtc().ToString([Globalization.CultureInfo]::InvariantCulture)
    if ($identity.StartFileTimeUtc -ne [string]$Manifest.StartFileTimeUtc -or
        $identity.StartFileTimeUtc -ne $recordedUtcFileTime -or
        -not [string]::Equals($identity.ImagePath, [string]$Manifest.ImagePath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Target PID/start time/image identity changed; observation and dumping refused.'
    }
    return $identity
}
function Assert-PauseBinaries($Manifest) {
    foreach ($record in $Manifest.BinaryFiles) {
        if (-not (Test-Path -LiteralPath $record.Path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $record.Path -Algorithm SHA256).Hash -ne $record.SHA256) {
            throw "On-disk build changed; dump refused: $($record.Role)"
        }
    }
}
function Assert-PauseManifest($Manifest, [string]$Directory) {
    if ($Manifest.SchemaVersion -ne 1 -or $Manifest.ProcessId -le 0 -or $Manifest.ProcessId -eq 32284 -or
        -not [IO.Path]::IsPathRooted([string]$Manifest.ImagePath) -or
        [IO.Path]::GetFileName($Manifest.ImagePath) -ne 'TheWorstHotelEver.exe' -or
        $Manifest.RunId -ne [IO.Path]::GetFileName($Directory.TrimEnd('\')) -or
        $Manifest.ObserverSeconds -lt 30 -or $Manifest.ObserverSeconds -gt 1800 -or
        $Manifest.SampleSeconds -lt 2 -or $Manifest.SampleSeconds -gt 5 -or
        $Manifest.SuspectAfterSeconds -ne 20 -or $Manifest.StartupHeartbeatGraceSeconds -ne 60 -or
        $Manifest.MaximumDumps -ne 2 -or $Manifest.DumpSpacingSeconds -ne 10 -or $Manifest.DumpFlags -ne '0x1924') {
        throw 'Unexpected launch manifest; observation refused.'
    }
    $createdFileTime = 0L
    if (-not [long]::TryParse([string]$Manifest.StartFileTimeUtc, [ref]$createdFileTime) -or $createdFileTime -le 0 -or
        ([datetime]$Manifest.StartUtc).ToUniversalTime().ToFileTimeUtc() -ne $createdFileTime) {
        throw 'Manifest start UTC and exact creation FILETIME disagree.'
    }
    if ($Manifest.HeartbeatPath -ne (Join-Path $Directory 'heartbeat.json') -or
        $Manifest.PlayerLog -ne (Join-Path $Directory 'Player.log')) {
        throw 'Evidence paths must stay inside this run directory.'
    }
    $imageDirectory = [IO.Path]::GetDirectoryName($Manifest.ImagePath)
    $requiredPaths = @{
        Launcher = $Manifest.ImagePath
        Gameplay = (Join-Path $imageDirectory 'TheWorstHotelEver_Data/Managed/WorstHotel.Runtime.dll')
        UnityPlayer = (Join-Path $imageDirectory 'UnityPlayer.dll')
    }
    $roles = @{}
    if (@($Manifest.BinaryFiles).Count -ne 3) { throw 'Three build binary hashes are required.' }
    foreach ($record in $Manifest.BinaryFiles) {
        if (-not $requiredPaths.ContainsKey([string]$record.Role) -or $roles.ContainsKey([string]$record.Role) -or
            $record.Length -le 0 -or $record.SHA256 -notmatch '^[a-fA-F0-9]{64}$' -or
            -not [string]::Equals([IO.Path]::GetFullPath([string]$record.Path),
                [IO.Path]::GetFullPath([string]$requiredPaths[$record.Role]), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Unexpected build identity/hash record.'
        }
        $roles[[string]$record.Role] = $true
    }
}
function Save-PauseEvidenceInventory([string]$Directory) {
    $rows = @(Get-ChildItem -LiteralPath $Directory -File | Where-Object { $_.Name -ne 'evidence-sha256.json' } | ForEach-Object {
        try {
            [pscustomobject]@{ File = $_.Name; Length = $_.Length; LastWriteUtc = $_.LastWriteTimeUtc.ToString('o')
                SHA256 = (Get-PauseSharedHash $_.FullName) }
        } catch { [pscustomobject]@{ File = $_.Name; Error = $_.Exception.Message } }
    })
    Write-PauseJson -Path (Join-Path $Directory 'evidence-sha256.json') -Value @{
        CapturedUtc = [DateTime]::UtcNow.ToString('o'); Files = $rows
        Note = 'Point-in-time hashes; the live player log/heartbeat or unfinished dump may continue changing.'
    }
}
if ($LibraryOnly) { return }
if (-not [Environment]::Is64BitProcess) { throw 'The observer requires 64-bit PowerShell.' }
if ([string]::IsNullOrWhiteSpace($RunDirectory)) { throw 'RunDirectory from the launcher is required.' }
$runPath = [IO.Path]::GetFullPath($RunDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $runPath 'launch.json') -Raw | ConvertFrom-Json
Assert-PauseManifest $manifest $runPath
$heartbeatPath = Join-Path $runPath 'heartbeat.json'
$playerLog = Join-Path $runPath 'Player.log'

if ($Mode -eq 'Capture') {
    if ($CaptureIndex -lt 1) { throw 'A numbered dump capture is required.' }
    $toolSmoke = $CaptureReason -eq 'ToolSmoke'
    $userReported = $CaptureReason -eq 'UserReportedHang'
    $resultName = if ($toolSmoke) { 'tool-smoke-dump-{0}-result.json' -f $CaptureIndex }
        elseif ($userReported) { 'user-reported-dump-{0}-result.json' -f $CaptureIndex }
        else { 'dump-{0}-result.json' -f $CaptureIndex }
    $dumpName = if ($toolSmoke) { 'healthy-tool-smoke-{0}.dmp' -f $CaptureIndex }
        elseif ($userReported) { 'user-reported-hang-{0}.dmp' -f $CaptureIndex }
        else { 'hang-suspect-{0}.dmp' -f $CaptureIndex }
    $resultPath = Join-Path $runPath $resultName
    $dumpPath = Join-Path $runPath $dumpName
    $captureClaim = [IO.File]::Open(($resultPath + '.claim'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $captureClaim.Dispose()
    $interpretation = if ($toolSmoke) { 'Explicit healthy owned-process dump-tool smoke; NOT crash or hang evidence.' }
        elseif ($userReported) { 'User reports a frozen picture. Capture during reported symptoms; heartbeat stall and root cause are NOT established.' }
        else { 'Suspected heartbeat stall; no root cause established.' }
    $capture = [ordered]@{ Index = $CaptureIndex; ProcessId = $manifest.ProcessId; BeginUtc = [DateTime]::UtcNow.ToString('o')
        Flags = '0x1924'; Status = 'Capturing'; Reason = $CaptureReason; Interpretation = $interpretation }
    try {
        Assert-PauseIdentity $manifest | Out-Null
        Assert-PauseBinaries $manifest
        $dbgHelp = Join-Path ([Environment]::SystemDirectory) 'dbghelp.dll'
        $dbgCore = Join-Path ([Environment]::SystemDirectory) 'dbgcore.dll'
        foreach ($library in @($dbgHelp, $dbgCore)) {
            $signature = Get-AuthenticodeSignature -LiteralPath $library
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
                throw "System dump library signature not trusted: $library"
            }
        }
        $capture['DumpCallBeginUtc'] = [DateTime]::UtcNow.ToString('o')
        Write-PauseJson -Path $resultPath -Value $capture
        [PauseInvestigationNative]::WriteDump([int]$manifest.ProcessId, [string]$manifest.StartFileTimeUtc,
            [string]$manifest.ImagePath, $dumpPath, $dbgHelp)
        $capture.Status = 'Captured'
        $capture['Length'] = (Get-Item -LiteralPath $dumpPath).Length
        $capture['SHA256'] = (Get-FileHash -LiteralPath $dumpPath -Algorithm SHA256).Hash
    } catch { $capture.Status = 'CaptureFailed'; $capture['Error'] = $_.Exception.ToString() }
    $capture['EndUtc'] = [DateTime]::UtcNow.ToString('o')
    $capture['PlayerWasTerminated'] = $false
    Write-PauseJson -Path $resultPath -Value $capture
    return
}

$clock = [Diagnostics.Stopwatch]::StartNew()
$samplePath = Join-Path $runPath 'process-samples.jsonl'
$eventPath = Join-Path $runPath 'observer-events.jsonl'
$lastSequence = [long]-1; $lastProgressSeconds = 0.0; $seenHeartbeat = $false
$lastHeartbeat = $null; $lastHeartbeatError = $null; $dumpCount = 0; $lastDumpSeconds = -100.0
$dumpWorker = $null; $suspected = $false; $startupReported = $false; $samples = 0
$endReason = 'ObserverTimeLimit'; $endError = $null; $target = $null
$claimPath = Join-Path $runPath 'observer.claim'
# A run has one observer for its entire lifetime, including after an observer exits.
# A duplicate manual invocation must not overwrite evidence or reset the two-dump limit.
$claim = [IO.File]::Open($claimPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $claimBytes = [Text.Encoding]::UTF8.GetBytes(('ObserverProcessId={0}; Utc={1}' -f $PID, [DateTime]::UtcNow.ToString('o')))
    $claim.Write($claimBytes, 0, $claimBytes.Length)
} finally { $claim.Dispose() }
try {
    Assert-PauseIdentity $manifest | Out-Null
    Assert-PauseBinaries $manifest
    $target = [Diagnostics.Process]::GetProcessById([int]$manifest.ProcessId)
    Add-PauseRecord $eventPath @{ Utc = [DateTime]::UtcNow.ToString('o'); Event = 'ObserverStarted'; ProcessId = $manifest.ProcessId }
    while ($clock.Elapsed.TotalSeconds -lt $manifest.ObserverSeconds) {
        $sampleStart = $clock.Elapsed.TotalSeconds
        $target.Refresh()
        if ($target.HasExited) { $endReason = 'PlayerExited'; break }
        Assert-PauseIdentity $manifest | Out-Null
        $heartbeatReadError = $null
        if (Test-Path -LiteralPath $heartbeatPath -PathType Leaf) {
            try {
                $heartbeat = Read-PauseSharedText $heartbeatPath | ConvertFrom-Json
                if ($heartbeat.schemaVersion -ne 1 -or $heartbeat.runId -ne $manifest.RunId -or
                    $heartbeat.processId -ne $manifest.ProcessId -or $null -eq $heartbeat.sequence -or
                    [long]$heartbeat.sequence -lt $lastSequence) { throw 'Heartbeat identity/schema/sequence rejected.' }
                if ([long]$heartbeat.sequence -gt $lastSequence) {
                    $lastSequence = [long]$heartbeat.sequence; $lastProgressSeconds = $clock.Elapsed.TotalSeconds
                    $seenHeartbeat = $true; $lastHeartbeat = $heartbeat
                    if ($suspected) {
                        Add-PauseRecord $eventPath @{ Utc = [DateTime]::UtcNow.ToString('o'); Event = 'HeartbeatProgressResumed'
                            Sequence = $lastSequence; Note = 'Progress resumed; no root cause or stability PASS inferred.' }
                        $suspected = $false
                    }
                }
            } catch { $heartbeatReadError = $_.Exception.Message }
        }
        if ($heartbeatReadError -and $heartbeatReadError -ne $lastHeartbeatError) {
            Add-PauseRecord $eventPath @{ Utc = [DateTime]::UtcNow.ToString('o'); Event = 'HeartbeatReadError'; Error = $heartbeatReadError }
        }
        $lastHeartbeatError = $heartbeatReadError
        $age = if ($seenHeartbeat) { $clock.Elapsed.TotalSeconds - $lastProgressSeconds } else { $null }
        if (-not $seenHeartbeat -and -not $startupReported -and $clock.Elapsed.TotalSeconds -ge 60) {
            Add-PauseRecord $eventPath @{ Utc = [DateTime]::UtcNow.ToString('o'); Event = 'StartupHeartbeatMissing'
                Note = 'No valid heartbeat observed. Build may lack diagnostics; automatic dumps are not armed.' }
            $startupReported = $true
        }
        $sample = [ordered]@{ Utc = [DateTime]::UtcNow.ToString('o'); ObserverElapsedSeconds = $clock.Elapsed.TotalSeconds
            ProcessId = $manifest.ProcessId; StartUtc = $manifest.StartUtc; IdentityValidated = $true
            CpuMilliseconds = $target.TotalProcessorTime.TotalMilliseconds; WorkingSetBytes = $target.WorkingSet64
            PrivateBytes = $target.PrivateMemorySize64; Handles = $target.HandleCount; Threads = $target.Threads.Count
            HeartbeatAgeSeconds = $age; LastHeartbeat = $lastHeartbeat; HeartbeatReadError = $heartbeatReadError }
        Add-PauseRecord $samplePath $sample
        $samples++
        if ($seenHeartbeat -and $age -ge 20) {
            if (-not $suspected) {
                Add-PauseRecord $eventPath @{ Utc = [DateTime]::UtcNow.ToString('o'); Event = 'SUSPECT_STALL'
                    HeartbeatAgeSeconds = $age; Note = 'Main-loop heartbeat stopped progressing. Recorder/storage failure is also possible; this is not a root cause.' }
                $suspected = $true
            }
            if ($null -ne $dumpWorker) {
                $dumpWorker.Refresh()
                if ($dumpWorker.HasExited) {
                    Add-PauseRecord $eventPath @{ Utc = [DateTime]::UtcNow.ToString('o'); Event = 'DumpHelperExited'
                        HelperProcessId = $dumpWorker.Id; ExitCode = $dumpWorker.ExitCode }
                    $dumpWorker.Dispose(); $dumpWorker = $null
                    # Leave at least ten seconds after the previous capture worker finished.
                    $lastDumpSeconds = $clock.Elapsed.TotalSeconds
                }
            }
            if ($dumpCount -lt 2 -and $null -eq $dumpWorker -and $clock.Elapsed.TotalSeconds - $lastDumpSeconds -ge 10) {
                Assert-PauseIdentity $manifest | Out-Null
                Assert-PauseBinaries $manifest
                $dumpCount++; $lastDumpSeconds = $clock.Elapsed.TotalSeconds
                $stamp = 'suspect-{0}' -f $dumpCount
                foreach ($source in @($playerLog, $heartbeatPath)) {
                    try {
                        if (Test-Path -LiteralPath $source) {
                            Copy-PauseSharedFile $source (Join-Path $runPath ($stamp + '-' + [IO.Path]::GetFileName($source)))
                        }
                    } catch {
                        Add-PauseRecord $eventPath @{ Utc = [DateTime]::UtcNow.ToString('o'); Event = 'LiveEvidenceCopyFailed'
                            File = [IO.Path]::GetFileName($source); Error = $_.Exception.Message }
                    }
                }
                $captureArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (ConvertTo-PauseArgument $PSCommandPath),
                    '-RunDirectory', (ConvertTo-PauseArgument $runPath), '-Mode', 'Capture', '-CaptureIndex', [string]$dumpCount)
                $dumpWorker = Start-Process -FilePath (Get-PauseShell) -ArgumentList $captureArguments -WindowStyle Hidden -PassThru `
                    -RedirectStandardOutput (Join-Path $runPath ($stamp + '-dump.stdout.txt')) `
                    -RedirectStandardError (Join-Path $runPath ($stamp + '-dump.stderr.txt'))
                Add-PauseRecord $eventPath @{ Utc = [DateTime]::UtcNow.ToString('o'); Event = 'DumpHelperStarted'; Index = $dumpCount
                    HelperProcessId = $dumpWorker.Id; Note = 'Dump capture may briefly suspend target threads; capture begin/end are recorded separately.' }
            }
        }
        $delay = [Math]::Max([double]0, [Math]::Min([double]$manifest.ObserverSeconds - $clock.Elapsed.TotalSeconds,
            [double]$manifest.SampleSeconds - ($clock.Elapsed.TotalSeconds - $sampleStart)))
        if ($delay -gt 0) { Start-Sleep -Milliseconds ([int]($delay * 1000)) }
    }
} catch {
    $endReason = 'ObserverError'; $endError = $_.Exception.ToString()
    if ($null -ne $target) {
        try { $target.Refresh(); if ($target.HasExited) { $endReason = 'PlayerExited' } } catch { }
    }
} finally {
    $exitCode = $null
    if ($null -ne $target) {
        try { if ($target.HasExited) { $exitCode = $target.ExitCode } } catch { }
        $target.Dispose()
    }
    $dumpStillRunning = $false; $dumpHelperId = $null
    if ($null -ne $dumpWorker) {
        try { $dumpWorker.Refresh(); $dumpStillRunning = -not $dumpWorker.HasExited; $dumpHelperId = $dumpWorker.Id } catch { }
        $dumpWorker.Dispose()
    }
    $summary = [ordered]@{ Utc = [DateTime]::UtcNow.ToString('o'); RunId = $manifest.RunId; Event = 'ObserverEnded'
        Reason = $endReason; Error = $endError; ObservedSeconds = $clock.Elapsed.TotalSeconds; Samples = $samples
        ValidHeartbeatObserved = $seenHeartbeat; LastHeartbeat = $lastHeartbeat; DumpAttempts = $dumpCount
        ActiveDumpHelper = $dumpStillRunning; DumpHelperProcessId = $dumpHelperId; PlayerExitCode = $exitCode
        PlayerWasTerminated = $false; Outcome = 'Evidence collected; no automatic PASS, crash diagnosis, or root-cause conclusion.' }
    Write-PauseJson -Path (Join-Path $runPath 'observer-ended.json') -Value $summary
    Add-PauseRecord $eventPath $summary
    Save-PauseEvidenceInventory $runPath
}
