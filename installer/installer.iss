; Version and CPU type are passed in by the build (/DMyAppVersion=2.1.0 /DMyArch=arm64).
; Single source for the version: the VERSION file (MAJOR.MINOR.PATCH). MyArch is x64 or arm64.
; Requires Windows 10 1809 (build 17763) or newer, including Windows 11. 64-bit and ARM64 only.
#ifndef MyAppVersion
  #define MyAppVersion "4.0.0"
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
#define MyArchLabel "ARM64"
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#define MyArchLabel "64-bit"
#endif
; Windows 10 1809 = build 17763 (minimum for the Windows App SDK)
MinVersion=10.0.17763

; If the updater is open, close it for the user instead of failing
CloseApplications=yes
RestartApplications=no

OutputDir=..\dist
; The -x64/-arm64 suffix is how the in-app self-updater finds the right file in a GitHub Release.
OutputBaseFilename=VlcNightlyUpdater-Setup-{#MyAppVersion}-{#MyArch}
SetupIconFile=..\VlcNightlyUpdater\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2/ultra64
SolidCompression=yes

; Dark, modern look (dark wizard needs Inno Setup 6.6+; older compilers fall back to the plain modern style)
#if Ver >= 0x06060000
WizardStyle=modern dark
#else
WizardStyle=modern
#endif
WizardSizePercent=115,115
WizardResizable=no
WizardImageFile=assets\wizard-large-164.bmp,assets\wizard-large-246.bmp,assets\wizard-large-328.bmp
WizardSmallImageFile=assets\wizard-small-55.bmp,assets\wizard-small-83.bmp,assets\wizard-small-110.bmp
WizardImageBackColor=$181818
WizardSmallImageBackColor=$242020
WizardImageAlphaFormat=none
ShowLanguageDialog=no
; No setup log: it would leave a "Setup Log ....txt" file in %TEMP% that the uninstaller can't know about.
SetupLogging=no
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WindowsVersionNotSupported=VLC Nightly Updater needs Windows 10 version 1809 or newer (or Windows 11).%n%nPlease update Windows and run this setup again.
ExitSetupTitle=Cancel setup

[InstallDelete]
; Clean update: wipe the old program files first. Settings live in %LOCALAPPDATA%\VlcNightlyUpdater
; and VLC's config in %APPDATA%\vlc, so neither is touched by an update.
Type: filesandordirs; Name: "{app}\*"

[UninstallDelete]
; Uninstall removes everything: anything the app created next to itself, plus any Windows crash dumps it caused.
; (Settings, cache, temp files, the scheduled task and registry keys are removed in [Code] below.)
Type: filesandordirs; Name: "{app}"
Type: files; Name: "{localappdata}\CrashDumps\VlcNightlyUpdater.exe.*.dmp"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu only. No desktop icon, no startup entry, no services. (The optional background check, off by default,
; creates one scheduled task from inside the app, and the uninstaller removes it.)
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

const
  Accent = $0088FF;      // VLC orange (BGR)
  Muted  = $B8B4B0;

procedure StyleLabel(L: TNewStaticText; Size: Integer; Bold: Boolean; Color: TColor);
begin
  L.Font.Size := Size;
  if Bold then L.Font.Style := [fsBold] else L.Font.Style := [];
  L.Font.Color := Color;
end;

// Segoe UI Variable ships with Windows 11 (build 22000+). Older Windows keeps the default Segoe UI.
function UseVariableFont(): Boolean;
var
  V: TWindowsVersion;
begin
  GetWindowsVersionEx(V);
  Result := V.Build >= 22000;
end;

procedure InitializeWizard();
begin
  Mode := DetectMode();

  // Typography: the Windows 11 UI font where it exists (Segoe UI Variable), Segoe UI on Windows 10.
  if UseVariableFont() then
  begin
    WizardForm.Font.Name := 'Segoe UI Variable Text';
    WizardForm.WelcomeLabel1.Font.Name := 'Segoe UI Variable Display';
    WizardForm.FinishedHeadingLabel.Font.Name := 'Segoe UI Variable Display';
    WizardForm.PageNameLabel.Font.Name := 'Segoe UI Variable Display';
  end;

  StyleLabel(WizardForm.WelcomeLabel1, 20, True, Accent);
  WizardForm.WelcomeLabel1.Height := ScaleY(40);
  WizardForm.WelcomeLabel2.Top := WizardForm.WelcomeLabel1.Top + WizardForm.WelcomeLabel1.Height + ScaleY(10);
  WizardForm.WelcomeLabel2.Height := WizardForm.WelcomePage.Height - WizardForm.WelcomeLabel2.Top - ScaleY(8);
  WizardForm.WelcomeLabel2.Font.Size := 10;
  WizardForm.WelcomeLabel2.Font.Color := Muted;

  StyleLabel(WizardForm.FinishedHeadingLabel, 20, True, Accent);
  WizardForm.FinishedHeadingLabel.Height := ScaleY(40);
  WizardForm.FinishedLabel.Top := WizardForm.FinishedHeadingLabel.Top + WizardForm.FinishedHeadingLabel.Height + ScaleY(10);
  WizardForm.FinishedLabel.Font.Size := 10;
  WizardForm.FinishedLabel.Font.Color := Muted;

  StyleLabel(WizardForm.PageNameLabel, 12, True, Accent);

  case Mode of
    0: begin
         WizardForm.WelcomeLabel1.Caption := 'Welcome to ' + '{#MyAppName}';
         WizardForm.WelcomeLabel2.Caption :=
           'Keeps your VLC nightly fresh with a single click.' + #13#10 + #13#10 +
           '    Only runs when you open it. Background update checks are off unless you turn them on.' + #13#10 + #13#10 +
           '    Installs just for you. No admin rights needed.' + #13#10 + #13#10 +
           '    Never touches your VLC settings.' + #13#10 + #13#10 +
           '    Built for {#MyArchLabel} Windows 10 and 11.' + #13#10 + #13#10 + #13#10 +
           'Setup takes a few seconds.';
       end;
    1: begin
         WizardForm.Caption := 'Update {#MyAppName}';
         WizardForm.WelcomeLabel1.Caption := 'Update available';
         WizardForm.WelcomeLabel2.Caption :=
           OldVersion + '   >   ' + NewVersion + #13#10 + #13#10 +
           'This replaces the old program files with the new ones. ' +
           'Your updater settings and your VLC configuration are kept exactly as they are.' + #13#10 + #13#10 +
           'It only takes a few seconds.';
       end;
    2: begin
         WizardForm.Caption := 'Reinstall {#MyAppName}';
         WizardForm.WelcomeLabel1.Caption := 'Reinstall';
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
        begin
          WizardForm.PageNameLabel.Caption := 'Installing';
          WizardForm.PageDescriptionLabel.Caption := 'Setting up {#MyAppName} {#MyAppVersion} for you.';
        end
        else
        begin
          WizardForm.PageNameLabel.Caption := 'Updating';
          WizardForm.PageDescriptionLabel.Caption := 'Swapping in {#MyAppName} {#MyAppVersion}.';
        end;
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

// ---------- uninstall: remove every trace ----------
// Names below must match Services/BackgroundCheck.cs. Your VLC settings (%APPDATA%\vlc) are never touched.
const
  BgTaskName = 'VlcNightlyUpdater Background Check';
  BgAumid    = 'StrikerXR.VlcNightlyUpdater';

procedure RunHidden(const Exe, Params: String);
var
  Code: Integer;
begin
  Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function InitializeUninstall(): Boolean;
begin
  // Remove the scheduled task first so nothing restarts the app, then close it so its files can be deleted.
  RunHidden(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "' + BgTaskName + '" /F');
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExe}');
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Saved settings, cached VLC installers, state file
    DelTree(ExpandConstant('{localappdata}\VlcNightlyUpdater'), True, True, True);
    // Leftover self-update downloads
    DelTree(AddBackslash(GetEnv('TEMP')) + 'VlcNightlyUpdater', True, True, True);

    // Notification + click-to-open registration (only exists if background checks were ever turned on)
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\AppUserModelId\' + BgAumid);
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\vlcnightlyupdater');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\' + BgAumid);
  end;
end;
