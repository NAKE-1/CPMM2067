; Inno Setup script for CPMM2067
;
; Manual build:
;   dotnet publish src\CPMM2067.App -c Release -r win-x64 --self-contained true ^
;       /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
;       -o publish\win-x64
;   ISCC.exe packaging\install.iss
;
; CI build (.github/workflows/release.yml) passes /DMyAppVersion=<tag-stripped> on the command line.

#ifndef MyAppVersion
  #define MyAppVersion "0.2.0"
#endif

#define MyAppName "CPMM2067"
#define MyAppPublisher "CPMM2067 Project"
#define MyAppURL "https://github.com/NAKE-1/CPMM2067"
#define MyAppExeName "CPMM2067.App.exe"

[Setup]
AppId={{B5E6F9A2-7D2E-4C0D-9F3E-CPMM20677F00}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}.0
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=Output
OutputBaseFilename=CPMM2067-{#MyAppVersion}-setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "nxmhandler"; Description: "Register nxm:// protocol handler (lets Nexus 'Mod Manager Download' install into CPMM2067)"; GroupDescription: "Integrations:"

[Files]
Source: "..\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\nxm"; ValueType: string; ValueName: ""; ValueData: "URL:NXM Protocol"; Tasks: nxmhandler; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nxm"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Tasks: nxmhandler
Root: HKCU; Subkey: "Software\Classes\nxm\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: nxmhandler
Root: HKCU; Subkey: "Software\Classes\nxm\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --nxm ""%1"""; Tasks: nxmhandler

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
; Leave user data behind by default. Uninstaller prompts via dialog instead.
Type: filesandordirs; Name: "{app}\data\logs"
