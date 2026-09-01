param([switch]$UpdateLockFiles)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    function Invoke-DotNet([string[]]$TaskArguments) {
        & dotnet @TaskArguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($TaskArguments -join ' ')" }
    }
    $restoreArguments = @('restore', 'dabudi.sln')
    if ($UpdateLockFiles) { $restoreArguments += '--force-evaluate' }
    else { $restoreArguments += '--locked-mode' }
    Invoke-DotNet $restoreArguments
    Invoke-DotNet @('build', 'dabudi.sln', '-c', 'Release', '--no-restore')
    Invoke-DotNet @('tests/Dabudi.Tests/bin/Release/net8.0/Dabudi.Tests.dll')
    Invoke-DotNet @('publish', 'src/Dabudi.App/Dabudi.App.csproj', '-c', 'Release', '--no-restore', '-o', 'dist')
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the source commit.' }
    & "$PSScriptRoot/verify-exe.ps1" -Executable 'dist/dabudi.exe' -ExpectedCommit $commit
    & "$PSScriptRoot/smoke.ps1" -Executable 'dist/dabudi.exe' -OutputDirectory 'artifacts/smoke'
} finally { Pop-Location }
