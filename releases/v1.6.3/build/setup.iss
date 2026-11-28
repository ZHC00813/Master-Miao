[Setup]
#ifdef Validation
AppId=MasterMiao.Verify163
Uninstallable=no
UsePreviousAppDir=no
#else
AppId=MasterMiao.Workspace
#endif
AppName=喵师傅 Master Miao
AppVersion=1.6.3
DefaultDirName={localappdata}\Programs\MasterMiao
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\MasterMiao.ico
SetupIconFile=payload\MasterMiao.ico
OutputDir=output
#ifdef Validation
OutputBaseFilename=Validation163
#else
OutputBaseFilename=MasterMiao-1.6.3-Setup-x64
#endif
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=no
[Files]
Source: "payload\焊接体分组说明.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\Blankedge.SWHoleTools.BatchExporter.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\Blankedge.SWHoleTools.BatchExporter.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\Blankedge.SWHoleTools.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\Cleanup-HoleTools.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\Install-HoleTools.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MasterMiao.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MasterMiao.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MasterMiao.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MasterMiao.Pcb.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MasterMiao.PcbNative.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MasterMiao.PcbSwImport.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MasterMiao.StepMacro.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MasterMiao.Weld.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\PCB单实体连接说明.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\Register-Addin.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\RenderDrawingPdf.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\SolidWorks.Interop.sldworks.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\SolidWorks.Interop.swconst.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\SolidWorks.Interop.swpublished.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\THIRD_PARTY_NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "payload\Core\MasterMiao.exe"; DestDir: "{app}\Core"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\PcbEngine\*"; DestDir: "{app}\PcbEngine"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\PcbRuntime\*"; DestDir: "{app}\PcbRuntime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\SplitFaceColor.dll"; DestDir: "{app}"; Flags: ignoreversion
#ifndef Validation
[Icons]
Name: "{userprograms}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"
Name: "{userdesktop}\喵师傅"; Filename: "{app}\MasterMiao.exe"; IconFilename: "{app}\MasterMiao.ico"

#endif

