#define MyAppName "MeshCaddy"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "MeshCaddy"
#define MyAppExeName "MeshCaddy.exe"

[Setup]
AppId={{B35647E5-C77A-4F97-A8AF-C13BD04D5155}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\Releases
OutputBaseFilename=MeshCaddy-Setup-{#MyAppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
ChangesAssociations=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
SetupIconFile=..\Assets\Brand\meshcaddy.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "associatefiles"; Description: "Open STL and 3MF files with MeshCaddy"; GroupDescription: "File associations:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\MeshCaddy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\MeshCaddy"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\MeshCaddy"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\MeshCaddy.Model"; ValueType: string; ValueName: ""; ValueData: "3D model"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\MeshCaddy.Model\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\MeshCaddy.Model\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\.stl\OpenWithProgids"; ValueType: string; ValueName: "MeshCaddy.Model"; ValueData: ""; Tasks: associatefiles; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.3mf\OpenWithProgids"; ValueType: string; ValueName: "MeshCaddy.Model"; ValueData: ""; Tasks: associatefiles; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MeshCaddy"; Flags: nowait postinstall skipifsilent
