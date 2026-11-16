param([Parameter(Mandatory=$true)][string]$BaseDirectory,[Parameter(Mandatory=$true)][string]$RuntimeDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path $out){throw 'Choose a new empty output directory'}
New-Item -ItemType Directory -Path "$out/Core" -Force | Out-Null
$hostExe=Join-Path $BaseDirectory 'MasterMiao.exe'
if((Get-Item $hostExe).VersionInfo.FileVersion -ne '1.4.6.0'){throw 'Requires the verified 1.4.6 core'}
Get-ChildItem $BaseDirectory -File | Where-Object {$_.Name -notlike 'unins*' -and $_.Name -ne 'MasterMiao.exe'} | Copy-Item -Destination $out
Copy-Item $hostExe "$out/Core/MasterMiao.exe"
Copy-Item $RuntimeDirectory "$out/PcbRuntime" -Recurse
Copy-Item "$PSScriptRoot/pcb-step-simplifier" "$out/PcbEngine" -Recurse
Copy-Item "$PSScriptRoot/MasterMiao.exe.config" "$out/MasterMiao.exe.config" -Force
& (Join-Path $PSScriptRoot '../../tools/BuildIcon.ps1') -Source (Join-Path $PSScriptRoot '../../assets/MasterMiao-symbol.png') -Destination "$out/MasterMiao.ico"
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /target:library /platform:x64 /optimize+ "/out:$out/MasterMiao.Pcb.dll" "/reference:$out/Core/MasterMiao.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'PcbModule.cs')
if($LASTEXITCODE){throw 'PCB module compilation failed'}
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$out/MasterMiao.Workspace.exe" "/win32icon:$out/MasterMiao.ico" "/reference:$out/Core/MasterMiao.exe" "/reference:$out/MasterMiao.Pcb.dll" /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'ReleaseLauncher.cs')
if($LASTEXITCODE){throw 'Launcher compilation failed'}
Move-Item "$out/MasterMiao.Workspace.exe" "$out/MasterMiao.exe"
Write-Output $out
