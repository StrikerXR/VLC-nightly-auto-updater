# VLC Nightly Updater

Small WinUI 3 app (Windows 11, x64). Open it, it checks
https://artifacts.videolan.org/vlc/nightly-win64/ and, if there's a newer build than the one it
last recorded, downloads the `.exe`, verifies SHA512, and installs it silently (`/S`) over your
existing VLC. `%APPDATA%\vlc` is never touched (no uninstall step).

- First run with VLC already installed: assumes you're on the latest and records it.
- State: `%LOCALAPPDATA%\VlcNightlyUpdater\state.json` (delete to reset).
- Nothing runs in the background.

## Get the installer
Push to GitHub. Actions builds `VlcNightlyUpdater-Setup.exe` (an Inno Setup wizard).
Download the **VlcNightlyUpdater-Setup** artifact from the run (a zip containing only the exe),
or tag `v1.0.0` to attach the exe to a Release.

The wizard installs per-user to `%LOCALAPPDATA%\Programs\VLC Nightly Updater` (no admin needed),
adds a Start Menu entry, optional desktop shortcut, and a normal uninstaller.
