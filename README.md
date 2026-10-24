<div align="center">

# VLC Nightly Updater

**Keep VLC's nightly builds fresh with one click, or none.**

[![Release](https://img.shields.io/github/v/release/StrikerXR/VLC-nightly-auto-updater?color=ff8800)](https://github.com/StrikerXR/VLC-nightly-auto-updater/releases/latest)
![Windows](https://img.shields.io/badge/Windows-7%20%E2%80%93%2011-0078d4)
![Arch](https://img.shields.io/badge/CPU-x64%20%7C%20ARM64-555)

</div>

Open it and it checks VideoLAN's nightly channel for your CPU type. If there's a newer build, it downloads the
installer, verifies its SHA-512, and installs it silently over your existing VLC. Your VLC settings
(`%APPDATA%\vlc`) are never touched.

**Nothing runs in the background by default**: no startup entry, no services, no scheduled tasks.

## Download

Grab the setup from the [latest release](https://github.com/StrikerXR/VLC-nightly-auto-updater/releases/latest):

| Your Windows | Download |
|---|---|
| Windows 10 (1809) or newer, 64-bit | `VlcNightlyUpdater-Setup-X.Y.Z-x64.exe` |
| Windows 10/11 on ARM64 | `VlcNightlyUpdater-Setup-X.Y.Z-arm64.exe` |
| Windows 7 SP1, 8, 8.1 (64-bit) | `VlcNightlyUpdater-Setup-X.Y.Z-x64-legacy.exe` |

Per-user install, no admin rights needed. Verify downloads against `SHA256SUMS.txt` in the release.

## Features

- **Auto-update on open**, or switch to a manual "Update now" button
- **Resilient downloads**: resume after a dropped connection, retry with backoff, speed and time left, Cancel
- **Safe installs**: SHA-512 verified, VLC closed only right before installing and reopened afterwards, one automatic retry
- **What changed**: the VLC commits between your build and the latest, and what your last update brought
- **Optional AI summary** (off by default): a 3-bullet plain-language summary of those commits using your own Gemini key
- **Rollback**: install any of the 15 most recent builds; optionally keep the last 3 installers for offline use
- **Pause updates** until a date you choose: no checks, no installs, no notifications
- **Go back to stable**: installs the latest stable VLC and steps aside
- **Optional background notifications** (off by default)
- **Updates itself** from GitHub Releases (modern edition)
- 64-bit and ARM64 VLC supported; a Santa hat on the icon in December

## Windows support

| Edition | Windows | UI |
|---|---|---|
| Modern | 10 (1809) and newer. Tested on Windows 11 26H2. | WinUI 3, Mica Alt |
| Legacy | 7 SP1, 8, 8.1 (x64 only) | Plain WinForms on .NET 6 |

WinUI 3 can't run on Windows 7/8, so the Legacy edition is a small separate app that shares the same update engine.
It covers check, install, pause and the main options. It does not self-update and has no background notifications,
rollback picker or "go back to stable" button. Windows 7 also needs SP1 and a TLS 1.2 capable update level.

## Pause

Pick a date (or a quick preset) under *Pause VLC updates*. While paused the updater makes **no** VLC checks, the main
button reads "Paused" and is disabled, and background notifications stay quiet. Clear the pause to resume right away.

## Options

| Option | Default | What it does |
|---|---|---|
| Update automatically | on | Install as soon as the app opens |
| Open VLC after updating | off | |
| Reopen VLC if it was running | on | |
| Keep installers for offline rollback | on | Off deletes each installer after use and clears the cache |
| Keep this updater up to date | on | Self-update from GitHub Releases |
| Background checks | off | A notification when a new nightly is out |
| AI change summary | off | Needs your own Gemini API key |

State lives in `%LOCALAPPDATA%\VlcNightlyUpdater\state.json`; cached installers in `...\cache\<arch>`.

## AI change summary (optional)

Off by default, and while off the app never contacts Google. Turn it on, paste your own
[Gemini API key](https://aistudio.google.com/apikey) (stored encrypted with Windows DPAPI, current user only), and
*What changed* shows a 3-bullet summary above the raw commits. Only public commit titles are sent: no paths, usernames
or PC info. The model is editable; the default is `gemini-3.5-flash-lite`.

## Background checks (optional)

Options > *Check for VLC updates in the background*. When on, one per-user scheduled task
(`VlcNightlyUpdater Background Check`, no admin) runs `VlcNightlyUpdater.exe --background-check` at sign-in and every
6 hours. It has no window, shows a notification if a newer nightly exists (once per build), and **never installs
anything**. It also adds two small per-user registry keys so the notification can open the app. Turning it off or
uninstalling removes the task and keys.

## Architectures

| VLC build | Channel (`artifacts.videolan.org/vlc/...`) |
|---|---|
| 64-bit | `nightly-win64/` |
| ARM64 | `nightly-win64-arm-llvm/` |

"Auto" matches the VLC you already have. Switching architecture always asks first. If VideoLAN renames a channel, edit
the `Channels` table in `Services/UpdateService.cs`.

## Uninstall

Removes the program, saved settings, cached installers, self-update downloads, the background task and registry keys.
VLC and `%APPDATA%\vlc` are never touched.

## Build from source

Requires the .NET 8 SDK on Windows.

```powershell
# Modern edition
dotnet publish VlcNightlyUpdater/VlcNightlyUpdater.csproj -c Release -r win-x64 -p:Platform=x64 -o publish

# Legacy edition (Windows 7/8)
dotnet publish VlcNightlyUpdater.Legacy/VlcNightlyUpdater.Legacy.csproj -c Release -r win-x64 -o publish-legacy
```

Installers are built with [Inno Setup](https://jrsoftware.org/isinfo.php): `installer/installer.iss`
(`/DMyAppVersion=X.Y.Z /DMyArch=x64|arm64`, plus `/DMyEdition=legacy` for the Windows 7 setup).

## Releasing

`VERSION` holds the single version used for the app, installers, tag and Release. Edit it and run the
**Build & Release** workflow from the Actions tab. The release job waits for the builds (x64 required, ARM64 and
Legacy optional), writes `SHA256SUMS.txt`, and only then publishes, so self-update never sees a half-uploaded release.

## What's new in 2.3.0

- Default AI model is now `gemini-3.5-flash-lite`; the AI summary is no longer labelled experimental (still off by default). Better prompt: focuses on what users notice and skips build/CI noise.
- Raw commits in *What changed* now show and hide instantly, so the dialog no longer flashes or resizes.
- Works on Windows 10 (1809) and newer, plus a Legacy edition for Windows 7/8/8.1.
- New option to turn off installer caching.
- Pausing updates now really pauses: no checks, "Check again" is disabled until the pause ends.
- Mica Alt keeps its colours when the window loses focus.

## License

See the repository for license details. VLC is a trademark of VideoLAN; this project is not affiliated with VideoLAN.
