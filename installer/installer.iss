; Version and CPU type are passed in by the build (/DMyAppVersion=2.0.57 /DMyArch=arm64).
; Single source for the version: the VERSION file + run number. MyArch is x86, x64 or arm64.
#ifndef MyAppVersion
  #define MyAppVersion "2.0.0"
#endif
#ifndef MyArch
  #define MyArch "x64"
#endif
#define MyAppName "VLC Nightly Updater"
#define MyAppExe "VlcNightlyUpdater.exe"
#define MyAppUrl "https://github.com/StrikerXR/VLC-nightly-auto-updater"

[Setup]
; Keep this GUID forever: it's how a newer installer recognises (and updates) an older install.
AppId={{B3F1C2A4-7D5E-4A9B-9C21-5E8F0A6D1B77}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=StrikerXR
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Setup

; Per-user, no admin, no questions about where to install
DefaultDirName={autopf}\{#MyAppName}
UsePreviousAppDir=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
#if MyArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#elif MyArch == "x86"
; 32-bit build: installs as a 32-bit app and runs on any x86-compatible Windows (32-bit, 64-bit, ARM64 emulation)
ArchitecturesAllowed=x86compatible
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.19041

; If the updater is open, close it for the user instead of failing
CloseApplications=yes
RestartApplications=no

OutputDir=..\dist
; The -x86/-x64/-arm64 suffix is how the in-app self-updater finds the right file in a GitHub Release.
OutputBaseFilename=VlcNightlyUpdater-Setup-{#MyAppVersion}-{#MyArch}
SetupIconFile=..\VlcNightlyUpdater\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2/ultra64
SolidCompression=yes

; Dark, modern look
WizardStyle=modern dark
WizardImageFile=assets\wizard-large.bmp
WizardSmallImageFile=assets\wizard-small.bmp
WizardImageBackColor=$181818
WizardSmallImageBackColor=$202020

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[InstallDelete]
; Clean update: wipe the old program files first. Settings live in %LOCALAPPDATA%\VlcNightlyUpdater
; and VLC's config in %APPDATA%\vlc, so neither is touched.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu only. No desktop icon, no startup entry, no services, no scheduled tasks.
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Open {#MyAppName} now"; Flags: nowait postinstall skipifsilent
; Self-update: the running updater starts this setup with /SILENT /relaunch=1 and exits, so bring it back up.
Filename: "{app}\{#MyAppExe}"; Flags: nowait; Check: ShouldRelaunch

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B3F1C2A4-7D5E-4A9B-9C21-5E8F0A6D1B77}_is1';
  NewVersion = '{#MyAppVersion}';

var
  Mode: Integer;      // 0 = fresh install, 1 = update, 2 = same version (reinstall), 3 = older than installed
  OldVersion: String;

function ShouldRelaunch(): Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

function DetectMode(): Integer;
var
  PackedOld, PackedNew: Int64;
begin
  Result := 0;
  OldVersion := '';
  if not RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', OldVersion) then
    Exit;

  Result := 1;
  if StrToVersion(OldVersion, PackedOld) and StrToVersion(NewVersion, PackedNew) then
  begin
    if ComparePackedVersion(PackedOld, PackedNew) = 0 then
      Result := 2
    else if ComparePackedVersion(PackedOld, PackedNew) > 0 then
      Result := 3;
  end;
end;

procedure InitializeWizard();
begin
  Mode := DetectMode();

  case Mode of
    0: begin
         WizardForm.WelcomeLabel1.Caption := 'Welcome to ' + '{#MyAppName}';
         WizardForm.WelcomeLabel2.Caption :=
           'Keeps VLC nightly fresh with a single click.' + #13#10 + #13#10 +
           '- Only runs when you open it. Nothing in the background.' + #13#10 +
           '- Installs just for you. No admin rights needed.' + #13#10 +
           '- Never touches your VLC settings.' + #13#10 + #13#10 +
           'Setup takes a few seconds.';
       end;
    1: begin
         WizardForm.Caption := 'Update {#MyAppName}';
         WizardForm.WelcomeLabel1.Caption := 'Update {#MyAppName}';
         WizardForm.WelcomeLabel2.Caption :=
           'Version ' + OldVersion + '  to  ' + NewVersion + #13#10 + #13#10 +
           'This replaces the old program files with the new ones. ' +
           'Your updater settings and your VLC configuration are kept exactly as they are.' + #13#10 + #13#10 +
           'It only takes a few seconds.';
       end;
    2: begin
         WizardForm.Caption := 'Reinstall {#MyAppName}';
         WizardForm.WelcomeLabel1.Caption := 'Reinstall {#MyAppName}';
         WizardForm.WelcomeLabel2.Caption :=
           'Version ' + OldVersion + ' is already installed.' + #13#10 + #13#10 +
           'This refreshes its program files. Your settings are kept.';
       end;
    3: begin
         WizardForm.Caption := 'Install an older version?';
         WizardForm.WelcomeLabel1.Caption := 'Install an older version?';
         WizardForm.WelcomeLabel2.Caption :=
           'Version ' + OldVersion + ' is installed, and this setup contains the older version ' +
           NewVersion + '.' + #13#10 + #13#10 +
           'Continuing will replace it. Your settings are kept.';
       end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  case CurPageID of
    wpWelcome:
      case Mode of
        1: WizardForm.NextButton.Caption := 'Update';
        2: WizardForm.NextButton.Caption := 'Reinstall';
      else
        WizardForm.NextButton.Caption := 'Install';
      end;

    wpInstalling:
      begin
        if Mode = 0 then
          WizardForm.PageNameLabel.Caption := 'Installing'
        else
          WizardForm.PageNameLabel.Caption := 'Updating';
        WizardForm.PageDescriptionLabel.Caption := 'Hang tight, this only takes a moment.';
      end;

    wpFinished:
      begin
        if Mode = 0 then
        begin
          WizardForm.FinishedHeadingLabel.Caption := 'You''re all set';
          WizardForm.FinishedLabel.Caption :=
            '{#MyAppName} is installed. Find it in your Start menu whenever you want to update VLC.';
        end
        else
        begin
          WizardForm.FinishedHeadingLabel.Caption := 'Updated to ' + NewVersion;
          WizardForm.FinishedLabel.Caption := 'Your settings were kept. You''re good to go.';
        end;
      end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Dir := ExpandConstant('{localappdata}\VlcNightlyUpdater');
    if DirExists(Dir) and (not UninstallSilent()) then
    begin
      if SuppressibleMsgBox(
           'Also delete the updater''s saved settings?' + #13#10 + #13#10 +
           'Your VLC settings are never touched either way.',
           mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(Dir, True, True, True);
    end;
  end;
end;
