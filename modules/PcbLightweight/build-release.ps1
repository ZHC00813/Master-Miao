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
New-Item -ItemType Directory -Path "$out/PcbEngine/scripts" -Force | Out-Null
foreach($name in 'pcb_simplify.py','micro_details.py','edge_parameters.py','native_geometry.py') {
    Copy-Item "$PSScriptRoot/pcb-step-simplifier/scripts/$name" "$out/PcbEngine/scripts/$name"
}
Copy-Item "$PSScriptRoot/MasterMiao.exe.config" "$out/MasterMiao.exe.config" -Force
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /target:library /platform:x64 /optimize+ "/out:$out/MasterMiao.Pcb.dll" "/reference:$out/Core/MasterMiao.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'PcbModule.cs')
if($LASTEXITCODE){throw 'PCB module compilation failed'}
$weld=Join-Path $PSScriptRoot '../WeldGroups'
& $csc /nologo /target:library /platform:x64 /optimize+ "/out:$out/MasterMiao.Weld.dll" "/reference:$out/Core/MasterMiao.exe" "/reference:$out/SolidWorks.Interop.sldworks.dll" "/reference:$out/SolidWorks.Interop.swconst.dll" /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $weld 'WeldStore.cs') (Join-Path $weld 'WeldBridge.cs') (Join-Path $weld 'WeldModule.cs')
if($LASTEXITCODE){throw 'Weld module compilation failed'}
Copy-Item (Join-Path $weld '焊接体分组说明.txt') $out
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$out/MasterMiao.Workspace.exe" "/win32icon:$out/MasterMiao.ico" "/reference:$out/Core/MasterMiao.exe" "/reference:$out/MasterMiao.Pcb.dll" "/reference:$out/MasterMiao.Weld.dll" /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'ReleaseLauncher.cs')
if($LASTEXITCODE){throw 'Launcher compilation failed'}
Move-Item "$out/MasterMiao.Workspace.exe" "$out/MasterMiao.exe"
& $csc /nologo /target:exe /platform:x64 /optimize+ "/out:$out/MasterMiao.PcbSwImport.exe" "/reference:$out/SolidWorks.Interop.sldworks.dll" "/reference:$out/SolidWorks.Interop.swconst.dll" /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'SwSingleImport.cs')
if($LASTEXITCODE){throw 'PCB SolidWorks importer compilation failed'}
& $csc /nologo /target:exe /platform:x64 /optimize+ "/out:$out/MasterMiao.PcbNative.exe" "/reference:$out/SolidWorks.Interop.sldworks.dll" "/reference:$out/SolidWorks.Interop.swconst.dll" /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'PcbNativeBuild.cs')
if($LASTEXITCODE){throw 'PCB native builder compilation failed'}
Write-Output $out
