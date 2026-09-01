param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$ExpectedCommit = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$executablePath = (Resolve-Path $Executable).Path
$reader = [System.IO.BinaryReader]::new([System.IO.File]::OpenRead($executablePath))
try {
    if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'Missing DOS header.' }
    $reader.BaseStream.Position = 0x3C
    $peOffset = $reader.ReadInt32()
    $reader.BaseStream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x00004550) { throw 'Missing PE signature.' }
    if ($reader.ReadUInt16() -ne 0x8664) { throw 'The EXE is not x64.' }
    $reader.BaseStream.Position = $peOffset + 24
    if ($reader.ReadUInt16() -ne 0x20B) { throw 'The EXE is not PE32+.' }
    $reader.BaseStream.Position = $peOffset + 24 + 68
    if ($reader.ReadUInt16() -ne 2) { throw 'The EXE is not a Windows GUI application.' }
} finally { $reader.Dispose() }
$properties = [xml](Get-Content (Join-Path (Split-Path -Parent $PSScriptRoot) 'Directory.Build.props') -Raw)
$expectedVersion = [string]$properties.Project.PropertyGroup.Version
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
if ($info.FileVersion -ne "$expectedVersion.0") { throw "Unexpected file version: $($info.FileVersion)" }
if ($ExpectedCommit -and -not $info.ProductVersion.Contains($ExpectedCommit)) {
    throw "Product version is not linked to commit $ExpectedCommit : $($info.ProductVersion)"
}
if ((Get-Item $executablePath).Length -lt 20000000) { throw 'The published file is too small to contain the desktop runtime.' }
if (@(Get-ChildItem (Split-Path $executablePath) -Filter '*.dll').Count -ne 0) { throw 'Publication contains unpackaged DLLs.' }
$hash = (Get-FileHash $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()
$name = Split-Path $executablePath -Leaf
[System.IO.File]::WriteAllText("$executablePath.sha256", "$hash  $name`n", [System.Text.Encoding]::ASCII)
Write-Host "Verified $name — Windows GUI x64, $($info.ProductVersion), SHA-256 $hash"
