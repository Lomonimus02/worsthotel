param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Phase,
    [switch]$RegenerateScene
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logs = Join-Path $projectRoot 'Logs'
$prefix = "p02-$Phase"
$compileTask = if ($RegenerateScene) { 'Scene' } else { 'Compile' }
& (Join-Path $PSScriptRoot 'Unity.ps1') -Task $compileTask -LogName "$prefix-compile"
foreach ($suite in @(@{Task='Test'; File='editmode'}, @{Task='PlayTest'; File='playmode'})) {
    $suiteStartedUtc = [DateTime]::UtcNow
    & (Join-Path $PSScriptRoot 'Unity.ps1') -Task $suite.Task -LogName "$prefix-$($suite.File)"
    $source = Join-Path $logs "$($suite.File)-results.xml"
    if (-not (Test-Path -LiteralPath $source) -or (Get-Item -LiteralPath $source).LastWriteTimeUtc -lt $suiteStartedUtc) {
        throw "$prefix $($suite.File) did not write a fresh test result."
    }
    $evidence = Join-Path $logs "$prefix-$($suite.File).xml"
    Copy-Item -LiteralPath $source -Destination $evidence -Force
    [xml]$report = Get-Content -LiteralPath $evidence
    $run = $report.'test-run'
    if ($run.result -ne 'Passed' -or [int]$run.failed -ne 0 -or [int]$run.skipped -ne 0) {
        throw "$prefix $($suite.File) gate incomplete: result=$($run.result), failed=$($run.failed), skipped=$($run.skipped). See $evidence"
    }
    Write-Output "$prefix $($suite.File): $($run.passed)/$($run.total) passed; zero skipped."
}
Write-Output "$prefix compile and both test gates passed. Human-playtest conclusions are separate."
