[Setup]
AppId=MasterMiao.Workspace
AppName=喵师傅 Master Miao
AppVersion=1.5.0
DefaultDirName={localappdata}\Programs\MasterMiao
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\MasterMiao.ico
SetupIconFile=..\..\outputs\MasterMiao-1.5.0-final\MasterMiao.ico
OutputDir=..\..\outputs\release-1.5.0
OutputBaseFilename=MasterMiao-1.5.0-Setup-x64
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=no
[Files]
Source: "..\..\outputs\MasterMiao-1.5.0-final\MasterMiao.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.5.0-final\MasterMiao.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.5.0-final\MasterMiao.Pcb.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.5.0-final\MasterMiao.Weld.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.5.0-final\焊接体分组说明.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.5.0-final\Core\*"; DestDir: "{app}\Core"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\outputs\MasterMiao-1.5.0-final\PcbEngine\*"; DestDir: "{app}\PcbEngine"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\outputs\MasterMiao-1.5.0-final\PcbRuntime\*"; DestDir: "{app}\PcbRuntime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\outputs\MasterMiao-1.5.0-final\*"; DestDir: "{app}"; Excludes: "MasterMiao.exe,MasterMiao.exe.config,MasterMiao.Pcb.dll,MasterMiao.Weld.dll,MasterMiao.ico,MasterMiao.PcbSwImport.exe,MasterMiao.PcbNative.exe,MasterMiao.Workspace.exe"; Flags: onlyifdoesntexist
Source: "..\..\outputs\MasterMiao-1.5.0-final\MasterMiao.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.5.0-final\MasterMiao.PcbSwImport.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.5.0-final\MasterMiao.PcbNative.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.5.0-final\使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\outputs\MasterMiao-1.5.0-final\PCB单实体连接说明.txt"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{userprograms}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"
Name: "{userdesktop}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"
