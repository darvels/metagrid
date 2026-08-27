$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $projectRoot "Release"
$publishDir = Join-Path $releaseRoot "MetaGrid"
$zipPath = Join-Path $releaseRoot "MetaGrid-win-x64.zip"

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir | Out-Null

dotnet publish (Join-Path $projectRoot "src\MetaGrid.UI\MetaGrid.UI.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir

Get-ChildItem -LiteralPath $publishDir -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.xml') } |
    Remove-Item -Force

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$tempZipRoot = Join-Path $releaseRoot "_ziproot"
if (Test-Path $tempZipRoot) {
    Remove-Item -LiteralPath $tempZipRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $tempZipRoot | Out-Null
Copy-Item -LiteralPath $publishDir -Destination $tempZipRoot -Recurse

Compress-Archive -Path (Join-Path $tempZipRoot "MetaGrid") -DestinationPath $zipPath -CompressionLevel Optimal

Remove-Item -LiteralPath $tempZipRoot -Recurse -Force
