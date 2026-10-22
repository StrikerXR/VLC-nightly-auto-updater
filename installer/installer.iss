#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef RepoRoot
  #define RepoRoot ".."
#endif
#define MyAppName "VLC Nightly Updater"
#define MyAppExe "VlcNightlyUpdater.exe"

[Setup]
AppId={{B3F1C2A4-7D5E-4A9B-9C21-5E8F0A6D1B77}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Private
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#RepoRoot}\dist
OutputBaseFilename=VlcNightlyUpdater-Setup
SetupIconFile={#RepoRoot}\VlcNightlyUpdater\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#RepoRoot}\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
