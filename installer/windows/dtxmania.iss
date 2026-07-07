; DTXManiaCX Windows installer (Inno Setup 6)
;
; Build with:
;   iscc /DMyAppVersion=<x.y.z> /DSourceDir=<absolute\path\to\publish\win> installer\windows\dtxmania.iss
;
; MyAppVersion and SourceDir MUST be passed on the command line. The #error
; guards below fail the compile clearly if either is missing.

#ifndef MyAppVersion
  #error "MyAppVersion must be defined (pass /DMyAppVersion=x.y.z to iscc)"
#endif

#ifndef SourceDir
  #error "SourceDir must be defined (pass an absolute /DSourceDir=<path\to\publish\win> to iscc; Inno Setup resolves Source: relative to this .iss file's directory)"
#endif

#define MyAppName      "DTXManiaCX"
#define MyAppPublisher "DTXManiaCX"
#define MyAppExeName   "DTXMania.Game.Windows.exe"
#define MyAppURL       "https://github.com/chanwaichan/DTXmaniaCX"

[Setup]
; AppId must stay stable across versions so upgrades replace the old install.
; Regenerate once if you want a unique value, then never change it.
AppId={{8F4C6B7E-1D2A-4E3F-9B5C-D2A0E7F3B1C9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=DTXMania-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained game files from the win-x64 publish output
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

; Default System skin → per-user app-data so the game finds it on first launch.
; {localappdata} resolves to %LOCALAPPDATA%, matching AppPaths.GetDefaultSystemSkinRoot().
; Path is relative to this .iss file: installer\windows\..\..\System -> repo-root System\
; onlyifdoesntexist: seed defaults on first install only; never overwrite a user's
; customized skin on upgrade. uninsneveruninstall: never wipe the user's skin dir.
Source: "..\..\System\*"; DestDir: "{localappdata}\DTXManiaCX\System"; Flags: recursesubdirs createallsubdirs ignoreversion uninsneveruninstall onlyifdoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
