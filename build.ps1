param([switch]$Test)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found' }
$out=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$source=@(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /codepage:65001 ('/out:'+(Join-Path $out 'SCUMLabLauncher.exe')) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $source
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
if ($Test) {
    $testRoot=Join-Path $root ('build\self-test-'+[guid]::NewGuid().ToString('N'))
    $testProcess=Start-Process -FilePath (Join-Path $out 'SCUMLabLauncher.exe') -ArgumentList @('--self-test',('"'+$testRoot+'"')) -WindowStyle Hidden -Wait -PassThru
    $testReport=Join-Path $testRoot 'result.json'
    if (Test-Path -LiteralPath $testReport) { Get-Content -LiteralPath $testReport }
    if ($testProcess.ExitCode -ne 0) { throw 'Launcher self-test failed' }
}
Get-FileHash -LiteralPath (Join-Path $out 'SCUMLabLauncher.exe') -Algorithm SHA256 | Select-Object Path,Hash
