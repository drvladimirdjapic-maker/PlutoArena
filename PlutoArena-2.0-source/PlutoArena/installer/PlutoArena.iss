; Pluto Arena installer (Inno Setup 6)
; Build it with publish.ps1 - that script prepares the "payload" folder
; and then runs this file through ISCC.exe.

#define MyAppName "Pluto Arena"
#define MyAppVersion "2.0"
#define MyAppPublisher "Pluto Arena"
#define MyAppExe "PlutoArena.exe"

[Setup]
AppId={{B6E3C1D2-4F7A-4C1E-9B8D-2A5F7E3C9D41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

; Per-user install, no administrator rights needed.
; (PlutoArena.ini and PlutoArena.log are written next to the exe.)
DefaultDirName={localappdata}\Programs\PlutoArena
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
WizardStyle=modern
DisableProgramGroupPage=yes

OutputDir={#SourcePath}\output
OutputBaseFilename=PlutoArena-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes

UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExe}
SetupIconFile={#SourcePath}\..\pluto-arena.ico
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
; Pluto Arena itself (self-contained, .NET is included)
Source: "{#SourcePath}\payload\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Difficulty DLLs (Original + tuned bridge for Medium ... Very Easy)
Source: "{#SourcePath}\payload\modes\*"; DestDir: "{app}\modes"; Flags: ignoreversion skipifsourcedoesntexist

; Pluto Remastered bridge (native\bin + native\scripts + licenses)
Source: "{#SourcePath}\payload\native\*"; DestDir: "{app}\native"; Flags: ignoreversion recursesubdirs createallsubdirs

; Arena maps - copied into StarCraft\Maps\000_PlutoArena by the Setup window
Source: "{#SourcePath}\payload\maps\*"; DestDir: "{app}\maps"; Flags: ignoreversion skipifsourcedoesntexist

Source: "{#SourcePath}\README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{group}\README"; Filename: "{app}\README.txt"

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Start Pluto Arena"; Flags: postinstall nowait skipifsilent

[InstallDelete]
; Old difficulty DLLs from version 1.0 (replaced by the tuned bridge)
Type: files; Name: "{app}\modes\pluto-scr-medium.dll"
Type: files; Name: "{app}\modes\pluto-scr-easy.dll"
Type: files; Name: "{app}\modes\pluto-scr-easyplus.dll"
Type: files; Name: "{app}\modes\pluto-scr-veryeasy.dll"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\native\runtime-multiplayer"
Type: files; Name: "{app}\native\bin\pluto-scr.dll"
Type: files; Name: "{app}\PlutoArena.ini"
Type: files; Name: "{app}\PlutoArena.log"
