[Setup]
AppId=MasterMiao.Workspace
AppName=喵师傅 Master Miao
AppVersion=1.4.7
DefaultDirName={localappdata}\Programs\MasterMiao
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\MasterMiao.ico
SetupIconFile=..\..\outputs\MasterMiao-1.4.7\MasterMiao.ico
OutputDir=..\..\outputs\release-1.4.7
OutputBaseFilename=MasterMiao-1.4.7-Setup-x64
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=no
[Files]
Source: "..\..\outputs\MasterMiao-1.4.7\MasterMiao.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.4.7\MasterMiao.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.4.7\MasterMiao.Pcb.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.4.7\Core\*"; DestDir: "{app}\Core"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\outputs\MasterMiao-1.4.7\PcbEngine\*"; DestDir: "{app}\PcbEngine"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\outputs\MasterMiao-1.4.7\PcbRuntime\*"; DestDir: "{app}\PcbRuntime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\outputs\MasterMiao-1.4.7\*"; DestDir: "{app}"; Excludes: "MasterMiao.exe,MasterMiao.exe.config,MasterMiao.Pcb.dll"; Flags: onlyifdoesntexist
[Icons]
Name: "{userprograms}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"
Name: "{userdesktop}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"
