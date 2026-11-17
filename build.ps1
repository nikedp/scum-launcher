param([switch]$Test,[switch]$Installer)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found' }
$out=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $out -Force | Out-Null

# WebView2 SDK (NuGet, pinned + SHA256). Its DLLs are embedded into the EXE, nothing is shipped next to it.
$wvVersion='1.0.4258.31'
$wvSha256='56f7f4b8bf9aee4b8efefbbdd4f67d5f74ebd1b100ed0806da71bf76af481aa9'
$pkgRoot=Join-Path $root ('build\packages\webview2-'+$wvVersion)
$nupkg=$pkgRoot+'.nupkg'
if (!(Test-Path -LiteralPath (Join-Path $pkgRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'))) {
    New-Item -ItemType Directory -Path (Split-Path $nupkg) -Force | Out-Null
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri ('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/'+$wvVersion+'/microsoft.web.webview2.'+$wvVersion+'.nupkg') -OutFile $nupkg
    if ((Get-FileHash -LiteralPath $nupkg -Algorithm SHA256).Hash -ne $wvSha256) { throw 'WebView2 SDK package hash mismatch' }
    if (Test-Path -LiteralPath $pkgRoot) { Remove-Item -LiteralPath $pkgRoot -Recurse -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($nupkg,$pkgRoot)
}
$wvCore=Join-Path $pkgRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'
$wvForms=Join-Path $pkgRoot 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'
$wvLoader=Join-Path $pkgRoot 'runtimes\win-x64\native\WebView2Loader.dll'

$helper=Join-Path $out 'SCUMLabUpdateHelper.exe'
& $compiler /nologo /target:winexe /main:SCUMLab.UpdateHelperProgram /platform:x64 /optimize+ /codepage:65001 ('/out:'+$helper) /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $root 'src/SelfUpdateProtocol.cs') (Join-Path $root 'src/UpdateHelperProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Self-update helper compilation failed' }

$source=@(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File | ForEach-Object FullName)
$resources=@(('/resource:'+$helper+',SCUMLab.Updater.exe'),('/resource:'+$wvCore+',lib/Microsoft.Web.WebView2.Core.dll'),('/resource:'+$wvForms+',lib/Microsoft.Web.WebView2.WinForms.dll'),('/resource:'+$wvLoader+',lib/WebView2Loader.dll'))
$ui=Join-Path $root 'ui'
foreach ($file in Get-ChildItem -LiteralPath $ui -File -Recurse) {
    $name='ui/'+$file.FullName.Substring($ui.Length+1).Replace('\','/')
    if ($name -match '[,"]') { throw ('Unsupported UI file name: '+$name) }
    $resources+=('/resource:'+$file.FullName+','+$name)
}
& $compiler /nologo /target:winexe /main:SCUMLab.Program /platform:x64 /optimize+ /warn:4 /codepage:65001 ('/win32icon:'+(Join-Path $root 'installer\app.ico')) ('/out:'+(Join-Path $out 'SCUMLabLauncher.exe')) $resources /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll ('/reference:'+$wvCore) ('/reference:'+$wvForms) $source
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }

if ($Test) {
    $testRoot=Join-Path $root ('build\self-test-'+[guid]::NewGuid().ToString('N'))
    $testProcess=Start-Process -FilePath (Join-Path $out 'SCUMLabLauncher.exe') -ArgumentList @('--self-test',('"'+$testRoot+'"')) -WindowStyle Hidden -Wait -PassThru
    $testReport=Join-Path $testRoot 'result.json'
    if (Test-Path -LiteralPath $testReport) { Get-Content -LiteralPath $testReport }
    if ($testProcess.ExitCode -ne 0) { throw 'Launcher self-test failed' }
}

if ($Installer) {
    # Evergreen bootstrapper from Microsoft (~2 MB); accepted only with a valid Microsoft signature.
    $bootstrapper=Join-Path $out 'MicrosoftEdgeWebview2Setup.exe'
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper
    $sig=Get-AuthenticodeSignature -LiteralPath $bootstrapper
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'WebView2 bootstrapper signature check failed' }
    $iscc=@((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$iscc) {
        & choco install innosetup --no-progress -y | Out-Host
        $iscc=@((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$iscc) { throw 'Inno Setup 6 (ISCC.exe) not found' }
    }
    $version=(Get-Item -LiteralPath (Join-Path $out 'SCUMLabLauncher.exe')).VersionInfo.ProductVersion
    & $iscc /Qp ('/DAppVersion='+$version) (Join-Path $root 'installer\SCUMLabSetup.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
    Get-FileHash -LiteralPath (Join-Path $out 'SCUMLabSetup.exe') -Algorithm SHA256 | Select-Object Path,Hash
}
Get-FileHash -LiteralPath (Join-Path $out 'SCUMLabLauncher.exe') -Algorithm SHA256 | Select-Object Path,Hash
