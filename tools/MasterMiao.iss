[Setup]
AppId=MasterMiao.Workspace
AppName=喵师傅 Master Miao
AppVersion=1.4.1
DefaultDirName={localappdata}\Programs\MasterMiao
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
DefaultGroupName=Master Miao
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\MasterMiao.ico
SetupIconFile=..\build\MasterMiao.ico
OutputBaseFilename=MasterMiao-1.4.1-Setup-x64
Compression=lzma2
SolidCompression=yes
[Files]
Source: "..\build\MasterMiao.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\MasterMiao.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\MasterMiao.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\MasterMiao.StepMacro.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\Blankedge.SWHoleTools.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\Blankedge.SWHoleTools.BatchExporter.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\Blankedge.SWHoleTools.BatchExporter.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\SolidWorks.Interop.sldworks.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\SolidWorks.Interop.swconst.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\SolidWorks.Interop.swpublished.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\Install-HoleTools.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\Register-Addin.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\RenderDrawingPdf.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{userprograms}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"
Name: "{userdesktop}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"
