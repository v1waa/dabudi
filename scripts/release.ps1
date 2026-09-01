$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $properties = [xml](Get-Content 'Directory.Build.props' -Raw)
    $version = [string]$properties.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release version must be X.Y.Z.' }
    $request = Get-Content '.github/release.json' -Raw | ConvertFrom-Json
    if ($request.version -ne $version) { throw 'Release request and application version differ.' }
    $tag = "v$version"
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -ne $env:GITHUB_SHA) { throw 'Release must use the event commit.' }
    if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne $tag) { throw 'Tag and application version differ.' }
    $existingTag = & git tag --list $tag
    if ($existingTag) {
        $tagCommit = (& git rev-list -n 1 $tag).Trim()
        if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $commit) { throw 'Existing tag points to another commit.' }
    }
    & "$PSScriptRoot/verify-exe.ps1" -Executable 'dist/dabudi.exe' -ExpectedCommit $commit
    $changelog = Get-Content 'CHANGELOG.md' -Raw
    $pattern = '(?ms)^## ' + [regex]::Escape($version) + '\r?\n(.*?)(?=^## |\z)'
    $match = [regex]::Match($changelog, $pattern)
    if (-not $match.Success) { throw 'Release notes are missing from CHANGELOG.md.' }
    New-Item -ItemType Directory -Path 'artifacts' -Force | Out-Null
    $notesPath = Join-Path $projectRoot 'artifacts/release-notes.md'
    [System.IO.File]::WriteAllText($notesPath, $match.Groups[1].Value.Trim() + "`n`nWindows 10/11 x64. Скачайте dabudi.exe и запустите; установка .NET не требуется.`n", [System.Text.UTF8Encoding]::new($false))
    & gh release create $tag 'dist/dabudi.exe' 'dist/dabudi.exe.sha256' --repo $env:GITHUB_REPOSITORY --target $commit --title "dabudi $version" --notes-file $notesPath --latest
    if ($LASTEXITCODE -ne 0) { throw 'GitHub Release creation failed. Existing releases are never overwritten.' }
} finally { Pop-Location }
