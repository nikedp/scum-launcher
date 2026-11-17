param([switch]$Publish)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$manifestPath=Join-Path $root 'release/channel.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.tag -notmatch '^v[0-9A-Za-z._-]+$') { throw 'Invalid release manifest' }
# Mod payloads (PAK, registry delta) are hosted on our own server (Data.Feed, published by tools\claude\Publish-Feed.ps1
# in the SCUMLab project); GitHub releases carry only the launcher. release/channel.json is a gate manifest:
# launchers older than its minLauncherVersion (read from GitHub by 0.2.0 and earlier) are told to update first.
if ($manifest.minLauncherVersion -ne $manifest.version) { throw 'Gate manifest: minLauncherVersion must equal version' }
$out=Join-Path $root 'dist'
$exe=Join-Path $out 'SCUMLabLauncher.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the launcher first' }
$archive=Join-Path $out 'SCUMLabLauncher.zip'
Compress-Archive -LiteralPath @($exe,(Join-Path $root 'README.md')) -DestinationPath $archive -Force
$setup=Join-Path $out 'SCUMLabSetup.exe'
if ($Publish -and !(Test-Path -LiteralPath $setup)) { throw 'Build the installer first (build.ps1 -Installer)' }
$assets=@($archive,$manifestPath)+@(Get-ChildItem -LiteralPath $setup -ErrorAction SilentlyContinue | ForEach-Object FullName)
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
