param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$Project,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$watch=[Diagnostics.Stopwatch]::StartNew()
$argsList=@('--ui-project-screenshot',('"'+[IO.Path]::GetFullPath($Project)+'"'),('"'+[IO.Path]::Combine([IO.Path]::GetFullPath($Output),'ui.png')+'"'))
$process=Start-Process -FilePath $Executable -ArgumentList $argsList -WorkingDirectory (Split-Path -Parent $Executable) -WindowStyle Hidden -PassThru
$peak=0L
while(-not $process.HasExited){ $process.Refresh();$peak=[Math]::Max($peak,$process.PeakWorkingSet64);Start-Sleep -Milliseconds 100 }
$process.WaitForExit();$watch.Stop()
[pscustomobject]@{Operation='Load saved project and render UI (no SolidWorks)';Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,3);PeakWorkingSetMB=[Math]::Round($peak/1MB,2);ExitCode=$process.ExitCode}
