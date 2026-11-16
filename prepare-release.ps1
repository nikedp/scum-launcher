param([switch]$Publish)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$manifestPath=Join-Path $root 'release/channel.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.tag -notmatch '^v[0-9A-Za-z._-]+$') { throw 'Invalid release manifest' }
# Payloads above GitHub's 100 MB file limit (MH-6 PAK) are kept as release/parts/<name>.part0..N and joined here.
foreach ($file in @($manifest.files)) {
    $local=Join-Path $root ('release/payloads/'+$file.name)
    if (Test-Path -LiteralPath $local) { continue }
    $parts=@(Get-ChildItem -LiteralPath (Join-Path $root 'release/parts') -Filter ($file.name+'.part*') -File -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($parts.Count -eq 0) { throw ('Payload missing: '+$file.name) }
    $out=[IO.File]::Create($local)
    try { foreach ($p in $parts) { $in=[IO.File]::OpenRead($p.FullName); try { $in.CopyTo($out) } finally { $in.Dispose() } } } finally { $out.Dispose() }
}
foreach ($file in (@($manifest.files)+@($manifest.registryDelta))) {
    if ($file.name -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Unsafe payload name' }
    $local=Join-Path $root ('release/payloads/'+$file.name)
    if ((Get-Item -LiteralPath $local).Length -ne $file.bytes -or (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash -ne $file.sha256) { throw ('Payload verification failed: '+$file.name) }
}
$out=Join-Path $root 'dist'
$exe=Join-Path $out 'SCUMLabLauncher.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the launcher first' }
$archive=Join-Path $out 'SCUMLabLauncher.zip'
Compress-Archive -LiteralPath @($exe,(Join-Path $root 'README.md')) -DestinationPath $archive -Force
$assets=@($archive,$manifestPath)+(Get-ChildItem -LiteralPath (Join-Path $root 'release/payloads') -File | ForEach-Object FullName)
$checksums=Join-Path $out 'checksums.txt'
$assets | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+(Split-Path -Leaf $_) } | Set-Content -LiteralPath $checksums -Encoding utf8
if ($Publish) {
    if (!$env:GITHUB_REPOSITORY -or $env:GITHUB_REPOSITORY -ne 'nikedp/scum-launcher') { throw 'Unexpected destination repository' }
    & gh release view $manifest.tag --repo $env:GITHUB_REPOSITORY *> $null
    if ($LASTEXITCODE -eq 0) { Write-Host ('Release '+$manifest.tag+' already exists; immutable payloads left unchanged.'); exit 0 }
    & gh release create $manifest.tag --repo $env:GITHUB_REPOSITORY --draft --target $env:GITHUB_SHA --title ('SCUM Lab Launcher '+$manifest.version+' (experimental)') --notes-file (Join-Path $root 'release/NOTES.md')
    if ($LASTEXITCODE -ne 0) { throw 'Draft release creation failed' }
    & gh release upload $manifest.tag @assets $checksums --repo $env:GITHUB_REPOSITORY
    if ($LASTEXITCODE -ne 0) { throw 'Release asset upload failed' }
    & gh release edit $manifest.tag --repo $env:GITHUB_REPOSITORY --draft=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Release publishing failed' }
}
Write-Output $archive
