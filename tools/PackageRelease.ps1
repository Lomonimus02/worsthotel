param(
    [string]$BuildDirectory = 'Builds/Windows-0.3.1',
    [string]$ArchivePath = 'Builds/TheWorstHotelEver-0.3.1-Windows.zip',
    [string]$ReportPath = 'docs/verification/services-release-package.txt'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $BuildDirectory))
$archive = [IO.Path]::GetFullPath((Join-Path $projectRoot $ArchivePath))
$report = [IO.Path]::GetFullPath((Join-Path $projectRoot $ReportPath))
if (-not (Test-Path -LiteralPath (Join-Path $buildRoot 'TheWorstHotelEver.exe'))) { throw 'Windows player is missing.' }
if ($archive.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive must be outside the build directory.' }
if (Test-Path -LiteralPath $archive) { throw 'Archive already exists; choose a new output path to preserve the existing release.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/RELEASE_README_RU.txt') -Destination (Join-Path $buildRoot 'README-RU.txt')
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($buildRoot, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
$files = @(Get-ChildItem -LiteralPath $buildRoot -File -Recurse)
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
$checked = 0
try {
    $entries = @($zip.Entries | Where-Object { $_.Name.Length -gt 0 })
    if ($entries.Count -ne $files.Count) { throw 'ZIP file count does not match the complete build.' }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($buildRoot.Length + 1).Replace('\', '/')
        $entry = $zip.GetEntry($relative)
        if ($null -eq $entry -or $entry.Length -ne $file.Length) { throw "Missing or wrong-sized ZIP entry: $relative" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $entryHash = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($entryHash -ne $sourceHash) { throw "ZIP contents differ: $relative" }
        $checked++
    }
} finally { $zip.Dispose() }
$facts = @(
    'Outcome=PASS CompleteWindowsBuild=True EveryEntrySHA256Matches=True',
    ('Utc=' + [DateTime]::UtcNow.ToString('o')),
    ('Build=' + $BuildDirectory),
    ('Archive=' + $ArchivePath),
    ('Files=' + $checked),
    ('UncompressedBytes=' + ($files | Measure-Object Length -Sum).Sum),
    ('ArchiveBytes=' + (Get-Item -LiteralPath $archive).Length),
    ('ArchiveSHA256=' + (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash),
    ('ExecutableSHA256=' + (Get-FileHash -LiteralPath (Join-Path $buildRoot 'TheWorstHotelEver.exe') -Algorithm SHA256).Hash),
    ('GameplayAssemblySHA256=' + (Get-FileHash -LiteralPath (Join-Path $buildRoot 'TheWorstHotelEver_Data/Managed/WorstHotel.Runtime.dll') -Algorithm SHA256).Hash),
    'Includes=README-RU.txt, executable, UnityPlayer.dll, Data, Mono runtime and all other build files',
    'Verification=Archive integrity only; runtime and visual evidence are recorded separately in docs/VERIFICATION.md'
)
New-Item -ItemType Directory -Path (Split-Path -Parent $report) -Force | Out-Null
[IO.File]::WriteAllLines($report, $facts, [Text.UTF8Encoding]::new($false))
$facts
