$Version = '0.1.1'
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $projectRoot "Release"
$publishDir = Join-Path $releaseRoot "MetaGrid"
$updaterPublishDir = Join-Path $releaseRoot "MetaGrid.Updater"
$zipPath = Join-Path $releaseRoot "MetaGrid-v$Version-win-x64.zip"
$shaPath = "$zipPath.sha256"

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path $updaterPublishDir) {
    Remove-Item -LiteralPath $updaterPublishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir | Out-Null
New-Item -ItemType Directory -Path $updaterPublishDir | Out-Null

dotnet publish (Join-Path $projectRoot "src\MetaGrid.UI\MetaGrid.UI.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    /p:PublishSingleFile=true

dotnet publish (Join-Path $projectRoot "src\MetaGrid.Updater\MetaGrid.Updater.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $updaterPublishDir `
    /p:PublishSingleFile=true

Get-ChildItem -LiteralPath $publishDir -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.xml') } |
    Remove-Item -Force

Get-ChildItem -LiteralPath $updaterPublishDir -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.xml') } |
    Remove-Item -Force

Get-ChildItem -LiteralPath $updaterPublishDir -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $publishDir $_.Name) -Force
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path $shaPath) {
    Remove-Item -LiteralPath $shaPath -Force
}

$tempZipRoot = Join-Path $releaseRoot "_ziproot"
if (Test-Path $tempZipRoot) {
    Remove-Item -LiteralPath $tempZipRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $tempZipRoot | Out-Null
Copy-Item -LiteralPath $publishDir -Destination $tempZipRoot -Recurse

Compress-Archive -Path (Join-Path $tempZipRoot "MetaGrid") -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
Set-Content -LiteralPath $shaPath -Value "$hash  $(Split-Path $zipPath -Leaf)" -NoNewline

Remove-Item -LiteralPath $tempZipRoot -Recurse -Force
