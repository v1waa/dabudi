param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$OutputDirectory = 'artifacts/smoke'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$env:DABUDI_SMOKE_OUTPUT = (Resolve-Path $OutputDirectory).Path
$process = Start-Process -FilePath (Resolve-Path $Executable).Path -ArgumentList '--smoke-test' -PassThru
if (-not $process.WaitForExit(60000)) {
    $process.Kill()
    throw 'The WPF smoke test did not complete within 60 seconds.'
}
if ($process.ExitCode -ne 0) {
    $failure = Join-Path $env:DABUDI_SMOKE_OUTPUT 'smoke-failure.txt'
    if (Test-Path $failure) { Get-Content $failure | Write-Host }
    throw "The WPF smoke test failed: exit code $($process.ExitCode)."
}
$resultPath = Join-Path $env:DABUDI_SMOKE_OUTPUT 'smoke-result.json'
if (-not (Test-Path $resultPath)) { throw 'Smoke result file was not created.' }
$result = Get-Content $resultPath -Raw | ConvertFrom-Json
if (-not $result.passed -or $result.bindingErrors -ne 0 -or -not $result.delayedClick -or -not $result.hideKeepsRunning `
    -or -not $result.closeExits -or -not $result.overlaysDoNotOverlap -or -not $result.hotkeyMenu `
    -or -not $result.firstFramePosition -or -not $result.stopwatchRestartsFromZero `
    -or -not $result.stopwatchVisibilityIndependent -or -not $result.cpuSensorProcess) {
    throw 'Smoke validation did not pass.'
}
Write-Host 'WPF views, first-frame positions, independent stopwatch controls, CPU sensor process, delayed input and full exit passed.'
