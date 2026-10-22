# VLC Nightly Updater

Small WinUI 3 app (Windows 11, x64). Open it, it checks
https://artifacts.videolan.org/vlc/nightly-win64/ and, if there's a newer build than the one it
last recorded, downloads the `.exe`, verifies SHA512, and installs it silently (`/S`) over your
existing VLC. `%APPDATA%\vlc` is never touched (no uninstall step).

## Features
- Auto-update on open (or switch to a manual "Update now" button)
- First run with VLC installed: assumes you're on the latest and records it
- Installed / latest build tiles with readable dates, "last checked" time
- Install any of the 15 most recent builds (rollback or reinstall)
- Optional: launch VLC after updating, close the updater after updating
- One-click button to open the VLC settings folder
- Nothing runs in the background

State + options: `%LOCALAPPDATA%\VlcNightlyUpdater\state.json` (delete to reset).

## Get the installer
Push to GitHub. Actions builds `VlcNightlyUpdater-Setup.exe` (Inno Setup wizard).
Download the **VlcNightlyUpdater-Setup** artifact (a zip with only the exe), or tag `v1.0.0`
to attach the exe to a Release. Installs per-user, no admin needed.
