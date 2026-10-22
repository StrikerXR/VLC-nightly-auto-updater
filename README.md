# VLC Nightly Updater

Small WinUI 3 app (Windows 11, x64). Open it, it checks
https://artifacts.videolan.org/vlc/nightly-win64/ and, if there's a newer build than the one it
last recorded, downloads the `.exe`, verifies SHA512, and installs it silently (`/S`) over your
existing VLC. `%APPDATA%\vlc` is never touched (no uninstall step).

No background process, no startup entry, no services, no scheduled tasks.

## Features
- Auto-update on open (or switch to a manual "Update now" button)
- First run with VLC installed: assumes you're on the latest and records it
- Installed / latest build tiles with readable dates, "last checked" time
- Install any of the 15 most recent builds (rollback or reinstall)
- Optional: launch VLC after updating, close the updater after updating
- One-click button to open the VLC settings folder

State + options: `%LOCALAPPDATA%\VlcNightlyUpdater\state.json`.

## Versioning (one version for everything)
`VERSION` holds `MAJOR.MINOR` (e.g. `1.0`). Every build on `main` becomes `MAJOR.MINOR.<run number>`
(e.g. `1.0.57`) and that same number is used for the app, the installer, the exe name
(`VlcNightlyUpdater-Setup-1.0.57.exe`), the git tag (`v1.0.57`) and the GitHub Release.
Edit `VERSION` for bigger changes (`1.1`, `2.0`); the patch part is automatic.

## Releases
Every push to `main` (except docs-only changes) builds and publishes a Release with the installer.

## Installer
Dark, per-user (no admin), no folder/shortcut questions. Running a newer installer over an older
install says it's an **update** (`1.0.3 to 1.0.4`), replaces the program files cleanly, and keeps your
settings. Uninstalling asks whether to delete the updater's saved settings (default: keep).
Silent mode for scripts: `VlcNightlyUpdater-Setup-X.Y.Z.exe /VERYSILENT`.
