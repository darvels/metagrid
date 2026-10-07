param([string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$name = "MetaGrid-v$Version-win-x64"
$zip = Join-Path $root "artifacts/release/$name.zip"
$sha = "$zip.sha256"
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
if ((Get-Content -LiteralPath $sha -Raw).Trim() -cne "$hash  $name.zip") { throw 'Sidecar mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($entry in $archive.Entries) {
        $path = $entry.FullName.Replace('\', '/')
        if (-not $path.StartsWith("$name/") -or $path.Contains('..')) { throw "Unsafe package path: $path" }
        if ($path -match '(?i)(tmp/|userdata|settings\.json|logs/|backups/|TestResults|\.pdb$|\.har$|\.build$|\.log$|Assets/Guides/)') {
            throw "Development/private asset in package: $path"
        }
    }
} finally { $archive.Dispose() }
$extracted = Join-Path $root "tmp/release-inspection-$([Guid]::NewGuid().ToString('N'))"
Expand-Archive -LiteralPath $zip -DestinationPath $extracted
$package = Join-Path $extracted $name
foreach ($exe in @('MetaGrid.exe', 'MetaGrid.Updater.exe')) {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package $exe))
    if ($info.ProductVersion.Split('+')[0] -ne $Version -or $info.FileVersion -ne "$Version.0") { throw "Wrong binary version: $exe" }
}
if (-not (Test-Path (Join-Path $package 'WebView2Loader.dll'))) { throw 'WebView2 loader missing.' }
Write-Host "PACKAGE_VERIFIED $name.zip SHA256=$hash"
Write-Host "Extracted: $package"
