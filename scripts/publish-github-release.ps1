param([string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$name = "MetaGrid-v$Version-win-x64"
$zip = Join-Path $root "artifacts/release/$name.zip"
$sha = "$zip.sha256"
& (Join-Path $PSScriptRoot 'verify-release-package.ps1') -Version $Version
$env:GCM_INTERACTIVE = 'never'
$credential = "protocol=https`nhost=github.com`n`n" | git credential fill
if ($LASTEXITCODE -ne 0) { throw 'GitHub authentication unavailable.' }
$token = ($credential | Where-Object { $_.StartsWith('password=') }).Substring(9)
if (-not $token) { throw 'GitHub credential missing.' }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'MetaGrid-release'; 'X-GitHub-Api-Version' = '2022-11-28' }
$api = 'https://api.github.com/repos/darvels/metagrid'
$tag = "v$Version"
$head = (git rev-parse HEAD).Trim()
$tagCommit = (git rev-parse "$tag^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $head) { throw 'Tag must reference the intended release commit.' }
$existing = Invoke-RestMethod "$api/releases" -Headers $headers -TimeoutSec 30
if (@($existing | Where-Object tag_name -eq $tag).Count -gt 0) { throw 'Release already exists; do not overwrite it.' }
$notes = Get-Content (Join-Path $root "build/RELEASE_NOTES_$Version.md") -Raw
$payload = @{ tag_name=$tag; target_commitish=$head; name="MetaGrid $tag"; body=$notes; draft=$true; prerelease=$false } | ConvertTo-Json
$release = Invoke-RestMethod "$api/releases" -Method Post -Headers $headers -ContentType 'application/json' -Body $payload -TimeoutSec 30
$upload = $release.upload_url.Split('{')[0]
foreach ($file in @($zip, $sha)) {
    $filename = Split-Path $file -Leaf
    $asset = Invoke-RestMethod "$($upload)?name=$([Uri]::EscapeDataString($filename))" -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $file -TimeoutSec 180
    if ($asset.size -ne (Get-Item $file).Length -or $asset.name -ne $filename) { throw 'Uploaded asset verification failed; release left draft.' }
}
$release = Invoke-RestMethod "$api/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"prerelease":false,"make_latest":"true"}' -TimeoutSec 30
Write-Host "Published: $($release.html_url)"
$token = $null
$credential = $null
