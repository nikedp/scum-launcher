param([string]$LauncherExe=(Join-Path $PSScriptRoot 'dist\SCUMLabLauncher.exe'))
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $LauncherExe).Path
$root=Join-Path $PSScriptRoot ('build\updater-process-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$target=Join-Path $root 'SCUMLabLauncher.exe'
$stage=Join-Path $root 'staged.exe'
Copy-Item -LiteralPath $source -Destination $target
Copy-Item -LiteralPath $source -Destination $stage
$marker=[Text.Encoding]::ASCII.GetBytes('SCUMLab updater integration fixture')
$append=[IO.File]::Open($stage,[IO.FileMode]::Append,[IO.FileAccess]::Write)
try { $append.Write($marker,0,$marker.Length) } finally { $append.Dispose() }
$oldHash=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
$newHash=(Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash.ToLowerInvariant()
$helper=Join-Path $root 'SCUMLabUpdateHelper.exe'
$assembly=[Reflection.Assembly]::LoadFrom($source)
$resource=$assembly.GetManifestResourceStream('SCUMLab.Updater.exe')
if (!$resource) { throw 'Missing embedded updater helper' }
try { $output=[IO.File]::Open($helper,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write);try { $resource.CopyTo($output) } finally { $output.Dispose() } } finally { $resource.Dispose() }
$hostProcess=Start-Process -FilePath $target -ArgumentList '--wait-for-update-test' -WindowStyle Hidden -PassThru
$job=[ordered]@{target=$target;staged=$stage;oldSHA256=$oldHash;newSHA256=$newHash;expectedVersion=[Reflection.AssemblyName]::GetAssemblyName($source).Version.ToString(3);token=[guid]::NewGuid().ToString('N');parentPid=$hostProcess.Id;parentStartedUtcTicks=$hostProcess.StartTime.ToUniversalTime().Ticks;restart=$false}
$jobPath=Join-Path $root 'job.json'
$job | ConvertTo-Json | Set-Content -LiteralPath $jobPath -Encoding utf8
$worker=Start-Process -FilePath $helper -ArgumentList @('--apply',('"'+$jobPath+'"')) -WindowStyle Hidden -PassThru -Wait
$result=Get-Content -LiteralPath (Join-Path $root 'result.json') -Raw | ConvertFrom-Json
if ($worker.ExitCode -ne 0 -or !$result.ok -or !$hostProcess.HasExited) { throw ('Updater helper failed: '+$result.message) }
if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $newHash -or (Get-FileHash -LiteralPath $result.backup -Algorithm SHA256).Hash -ne $oldHash) { throw 'Updater process swap/backup hash mismatch' }
[ordered]@{ok=$true;checks=@('Embedded helper extracted','Real launcher process held target EXE open','Helper waited for parent exit','Locked EXE replaced after exit','Previous EXE backup hash verified');targetChanged=$true;liveGameChanged=$false;report=(Join-Path $root 'result.json')} | ConvertTo-Json -Depth 4
