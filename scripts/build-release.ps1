param(
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$uiProject = Join-Path $projectRoot 'src\MetaGrid.UI\MetaGrid.UI.csproj'
$testProject = Join-Path $projectRoot 'tests\MetaGrid.Tests\MetaGrid.Tests.csproj'
$releaseRoot = Join-Path $projectRoot 'artifacts\release'
$publishRoot = Join-Path $releaseRoot 'publish'
$packageName = "MetaGrid-v$Version-win-x64"
$packageRoot = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
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
    if (-not $resolved.StartsWith($allowedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside artifacts/release: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
Remove-ReleaseDirectory -PathToRemove $publishRoot
Remove-ReleaseDirectory -PathToRemove $packageRoot
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Host "Running tests..."
dotnet test $testProject -c Release

Write-Host "Publishing MetaGrid Release win-x64..."
dotnet publish $uiProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishRoot `
    /p:PublishSingleFile=true `
    /p:DebugSymbols=false `
    /p:DebugType=None

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

Copy-Item -LiteralPath $readmeSource -Destination (Join-Path $packageRoot 'README.md')

$licensePath = $licenseCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($licensePath) {
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $packageRoot (Split-Path $licensePath -Leaf))
}

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -Force

Write-Host ''
Write-Host 'Release artifacts created:'
Write-Host "Folder: $packageRoot"
Write-Host "Zip:    $zipPath"
