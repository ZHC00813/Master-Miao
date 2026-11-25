param([Parameter(Mandatory=$true)][string]$BasePackage,[Parameter(Mandatory=$true)][string]$ReleaseRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ReleaseRoot)
$base=[IO.Path]::GetFullPath($BasePackage)
$lines=@('[Setup]','AppId=MasterMiao.Workspace','AppName=喵师傅 Master Miao','AppVersion=1.6.2','DefaultDirName={localappdata}\Programs\MasterMiao','PrivilegesRequired=lowest','ArchitecturesAllowed=x64compatible','DisableProgramGroupPage=yes','UninstallDisplayIcon={app}\MasterMiao.ico',"SetupIconFile=$base\MasterMiao.ico", "OutputDir=$root\release",'OutputBaseFilename=MasterMiao-1.6.2-Setup-x64','Compression=lzma2/fast','SolidCompression=yes','CloseApplications=no','[Files]')
$sources=@{}
foreach($name in @('MasterMiao.exe.config','MasterMiao.ico','MasterMiao.Pcb.dll','MasterMiao.Weld.dll','MasterMiao.PcbSwImport.exe','MasterMiao.PcbNative.exe','MasterMiao.StepMacro.dll','Blankedge.SWHoleTools.BatchExporter.exe','Blankedge.SWHoleTools.BatchExporter.exe.config','SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll','SolidWorks.Interop.swpublished.dll','Install-HoleTools.ps1','Register-Addin.ps1','Cleanup-HoleTools.ps1','RenderDrawingPdf.ps1','使用说明.txt','焊接体分组说明.txt','PCB单实体连接说明.txt','THIRD_PARTY_NOTICE.md')){$sources[$name]=Join-Path $base $name}
$sources['MasterMiao.exe']=Join-Path $root 'package\MasterMiao.exe'
$sources['Blankedge.SWHoleTools.dll']=Join-Path $root 'package\Blankedge.SWHoleTools.dll'
foreach($name in $sources.Keys | Sort-Object){if(!(Test-Path -LiteralPath $sources[$name])){throw "Missing $name"};$lines+='Source: "'+$sources[$name]+'"; DestDir: "{app}"; Flags: ignoreversion'}
foreach($dir in @('Core','PcbEngine','PcbRuntime')){$lines+='Source: "'+$base+'\'+$dir+'\*"; DestDir: "{app}\'+$dir+'"; Flags: ignoreversion recursesubdirs createallsubdirs'}
$shipping=$lines+@('[Icons]','Name: "{userprograms}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"','Name: "{userdesktop}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"')
[IO.File]::WriteAllLines((Join-Path $root 'release162.iss'),$shipping,[Text.UTF8Encoding]::new($true))
$qa=$lines -replace '^AppId=.*','AppId=MasterMiao.ReleaseValidation162' -replace '^OutputBaseFilename=.*','OutputBaseFilename=MasterMiao-1.6.2-Validation'
$qa=$qa[0..($qa.IndexOf('[Files]')-1)]+@('Uninstallable=no','UsePreviousAppDir=no')+$qa[$qa.IndexOf('[Files]')..($qa.Count-1)]
[IO.File]::WriteAllLines((Join-Path $root 'validation162.iss'),$qa,[Text.UTF8Encoding]::new($true))
$sources.GetEnumerator()|Sort-Object Name|ForEach-Object{[pscustomobject]@{File=$_.Key;SHA256=(Get-FileHash -LiteralPath $_.Value).Hash}}|Export-Csv (Join-Path $root 'payload-manifest.csv') -NoTypeInformation -Encoding UTF8
