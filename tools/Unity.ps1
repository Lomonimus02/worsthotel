param(
    [ValidateSet('Configure','Compile','Scene','Test','PlayTest','Capture','Build')][string]$Task = 'Compile',
    [string]$UnityPath = '',
    [string]$LogName = '',
    [string]$TestFilter = '',
    [string]$BuildDirectory = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($BuildDirectory) {
    if ($Task -ne 'Build') { throw 'BuildDirectory requires -Task Build.' }
    $env:WORST_HOTEL_BUILD_OUTPUT = [IO.Path]::GetFullPath((Join-Path $projectRoot $BuildDirectory))
} else { $env:WORST_HOTEL_BUILD_OUTPUT = '' }
if (-not $UnityPath) {
    $hubEditors = Join-Path $env:APPDATA 'UnityHub/editors-v2.json'
    if (Test-Path -LiteralPath $hubEditors) {
        $editors = (Get-Content -LiteralPath $hubEditors -Raw | ConvertFrom-Json).data
        $editor = $editors | Where-Object version -eq '6000.3.2f1' | Select-Object -First 1
        if (-not $editor) { $editor = $editors | Where-Object version -like '6000.3.*' | Select-Object -First 1 }
        if ($editor) { $UnityPath = $editor.location[0] }
    }
}
if (-not $UnityPath -or -not (Test-Path -LiteralPath $UnityPath)) { throw 'Unity 6.3 LTS not found; supply -UnityPath path/to/Unity.exe.' }
$logDir = Join-Path $projectRoot 'Logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
if (-not $LogName) { $LogName = $Task.ToLowerInvariant() }
$logPath = Join-Path $logDir "$LogName.log"
# Keep this machine's large import/build scratch data off the nearly full system drive.
# This is a local optimization; clean checkouts do not require drive D.
if (Test-Path -LiteralPath 'D:\UnityCache\WorstHotelEver2') {
    $env:UPM_CACHE_ROOT = 'D:\UnityCache\WorstHotelEver2\PackageManager'
    $env:TEMP = 'D:\UnityCache\WorstHotelEver2\Scratch'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:UPM_CACHE_ROOT,$env:TEMP -Force | Out-Null
}
$unityArgs = @('-batchmode','-projectPath',"`"$projectRoot`"",'-logFile',"`"$logPath`"")
if ($Task -notin @('Capture','PlayTest')) { $unityArgs += '-nographics' }
switch ($Task) {
    'Configure' { $unityArgs += @('-quit','-executeMethod','WorstHotel.Editor.BuildAutomation.Configure') }
    'Compile' { $unityArgs += '-quit' }
    'Scene' { $unityArgs += @('-quit','-executeMethod','WorstHotel.Editor.PrototypeSceneBuilder.Build') }
    'Test' { $unityArgs += @('-runTests','-testPlatform','EditMode','-testResults',"`"$(Join-Path $logDir 'editmode-results.xml')`"") }
    'PlayTest' { $unityArgs += @('-runTests','-testPlatform','PlayMode','-testResults',"`"$(Join-Path $logDir 'playmode-results.xml')`"") }
    'Capture' { $unityArgs += @('-quit','-executeMethod','WorstHotel.Editor.VisualVerification.Capture') }
    'Build' { $unityArgs += @('-quit','-executeMethod','WorstHotel.Editor.BuildAutomation.BuildWindows') }
}
if ($TestFilter) {
    if ($Task -notin @('Test','PlayTest')) { throw 'TestFilter requires Test or PlayTest.' }
    if ($TestFilter.Contains('"')) { throw 'TestFilter cannot contain a quote.' }
    $unityArgs += @('-testFilter', "`"$TestFilter`"")
}
$process = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -WindowStyle Hidden -PassThru
# Wait only for the Editor, not its long-lived licensing/package-manager descendants.
$process.WaitForExit()
$errors = Select-String -LiteralPath $logPath -Pattern 'error CS\d+|Scripts have compiler errors|BuildFailedException|executeMethod.*could not|Aborting batchmode' -ErrorAction SilentlyContinue
if ($process.ExitCode -ne 0 -or $errors) { $errors | ForEach-Object Line; throw "Unity $Task failed (exit $($process.ExitCode)). See $logPath" }
Write-Output "Unity $Task finished successfully. Log: $logPath"
