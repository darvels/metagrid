param(
    [string]$Version = '0.2.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$uiProject = Join-Path $projectRoot 'src\MetaGrid.UI\MetaGrid.UI.csproj'
$updaterProject = Join-Path $projectRoot 'src\MetaGrid.Updater\MetaGrid.Updater.csproj'
$testProject = Join-Path $projectRoot 'tests\MetaGrid.Tests\MetaGrid.Tests.csproj'
$releaseRoot = Join-Path $projectRoot 'artifacts\release'
$publishRoot = Join-Path $releaseRoot 'publish'
$updaterPublishRoot = Join-Path $releaseRoot 'publish-updater'
$packageName = "MetaGrid-v$Version-win-x64"
$packageRoot = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$shaPath = "$zipPath.sha256"
$readmeSource = Join-Path $projectRoot 'build\README.release.md'
$licenseCandidates = @(
    (Join-Path $projectRoot 'LICENSE'),
    (Join-Path $projectRoot 'LICENSE.txt'),
    (Join-Path $projectRoot 'LICENSE.md')
)

function Remove-ReleaseDirectory {
    param([string]$PathToRemove)

    if (-not (Test-Path $PathToRemove)) {
        return
    }

    $resolved = (Resolve-Path $PathToRemove).Path
    $allowedRoot = (Resolve-Path $releaseRoot).Path
    if (-not $resolved.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside artifacts/release: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
Remove-ReleaseDirectory -PathToRemove $publishRoot
Remove-ReleaseDirectory -PathToRemove $updaterPublishRoot
Remove-ReleaseDirectory -PathToRemove $packageRoot
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path $shaPath) {
    Remove-Item -LiteralPath $shaPath -Force
}

Write-Host "Running tests..."
dotnet test $testProject -c Release --blame-hang-timeout 30s
if ($LASTEXITCODE -ne 0) { throw 'Release test gate failed.' }

dotnet build (Join-Path $projectRoot 'MetaGrid.sln') -c Release -m:1 --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Release build gate failed.' }

Write-Host "Publishing MetaGrid Release win-x64..."
dotnet publish $uiProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishRoot `
    /p:PublishSingleFile=true `
    /p:DebugSymbols=false `
    /p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw 'UI publish failed.' }

Write-Host "Publishing MetaGrid updater helper..."
dotnet publish $updaterProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $updaterPublishRoot `
    /p:PublishSingleFile=true `
    /p:DebugSymbols=false `
    /p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed.' }

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null

Get-ChildItem -LiteralPath $publishRoot -File | ForEach-Object {
    if ($_.Extension -in @('.pdb', '.xml')) {
        return
    }

    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $packageRoot $_.Name)
}

Get-ChildItem -LiteralPath $publishRoot -Directory | ForEach-Object {
    if ($_.Name -in @('runtimes', 'Assets')) {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $packageRoot $_.Name) -Recurse
    }
}

Get-ChildItem -LiteralPath $updaterPublishRoot -File | ForEach-Object {
    if ($_.Extension -in @('.pdb', '.xml')) {
        return
    }

    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $packageRoot $_.Name)
}

Copy-Item -LiteralPath $readmeSource -Destination (Join-Path $packageRoot 'README.md')

$licensePath = $licenseCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($licensePath) {
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $packageRoot (Split-Path $licensePath -Leaf))
}

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -Force

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
Set-Content -LiteralPath $shaPath -Value "$hash  $(Split-Path $zipPath -Leaf)" -NoNewline

Write-Host ''
Write-Host 'Release artifacts created:'
Write-Host "Folder: $packageRoot"
Write-Host "Zip:    $zipPath"
Write-Host "SHA:    $shaPath"
