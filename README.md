# VLC Nightly Updater

Small WinUI 3 app for Windows 10 (2004+) and Windows 11, built for **32-bit, 64-bit and ARM64**. Open it and it
checks VideoLAN's nightly channel for your CPU type; if there's a newer build than the one it last recorded, it
downloads the `.exe`, verifies SHA512, and installs it silently (`/S`) over your existing VLC. `%APPDATA%\vlc` is
never touched (no uninstall step).

No background process, no startup entry, no services, no scheduled tasks.

## Features
- **Auto-update on open** (or switch to a manual "Update now" button)
- **Updates itself** from this repo's GitHub Releases (see below)
- **32-bit, 64-bit and ARM64**: the updater is built for all three, and it manages whichever VLC you have
- **Resilient downloads**: resumes after a dropped connection or a stall (HTTP Range), retries with backoff,
  shows speed and time left, has a Cancel button, and a half-finished download resumes next time
- **What's new**: lists the VLC commits between your build and the latest, and what your last update brought
  (from code.videolan.org; click a commit to open it)
- **Smarter install flow**: downloads while VLC keeps running, closes VLC only right before installing
  (gently, force-close only if you agree), reopens it afterwards, retries a failed install once, checks free disk
  space, and confirms vlc.exe really landed as the right architecture
- **Offline rollback**: the last 3 verified installers are cached; cached builds are marked in the picker
- First run with VLC installed: assumes you're on the latest and records it
- Installed / latest build tiles with readable dates and CPU type
- Install any of the 15 most recent builds (rollback or reinstall)
- Optional: launch VLC after updating, close the updater after updating
- One-click button to open the VLC settings folder

State + options: `%LOCALAPPDATA%\VlcNightlyUpdater\state.json`. Cached installers: `...\VlcNightlyUpdater\cache\<arch>`.

## Architectures
VLC publishes a separate nightly channel per CPU type, and the updater picks the right one:

| VLC build | Channel (`https://artifacts.videolan.org/vlc/...`) |
|-----------|-----------------------------------------------------|
| 64-bit    | `nightly-win64/` |
| 32-bit    | `nightly-win32-llvm/` (falls back to `nightly-win32/`) |
| ARM64     | `nightly-win64-arm-llvm/` |

"Auto" (the default) matches whatever VLC you already have, detected from `vlc.exe` itself. Otherwise it uses
your Windows CPU type. You can pick a different build in Options; switching architecture always asks first, even
with auto-update on. If VideoLAN renames a channel, edit the `Channels` table in `Services/UpdateService.cs`.

The updater itself ships as three setups (`-x86`, `-x64`, `-arm64`). Windows 11 is 64-bit only, so the 32-bit
updater is for 32-bit Windows 10 (it also runs on 64-bit Windows).

## Self-update
On open, the app asks `https://api.github.com/repos/StrikerXR/VLC-nightly-auto-updater/releases/latest`. If the tag
is newer than the running version, it downloads the setup that matches **its own** CPU type, checks it against the
release's `SHA256SUMS.txt`, runs it silently, and reopens itself. If the checksum file is missing, nothing is
installed. It only self-updates the installed copy (never a build-folder or debugger run), and a failed self-update
never blocks the VLC update. Turn it off in Options ("Keep this updater up to date automatically"), and you get
an "Update now" banner instead.

Updating from 1.x: 1.x can't self-update, so install a 2.0 setup once by hand. It upgrades the old install in place.

## Versioning (one version for everything)
`VERSION` holds `MAJOR.MINOR` (currently `2.0`). Every build on `main` becomes `MAJOR.MINOR.<run number>`
(e.g. `2.0.57`) and that same number is used for the app, the installers, the exe names
(`VlcNightlyUpdater-Setup-2.0.57-x64.exe`), the git tag (`v2.0.57`) and the GitHub Release.
Edit `VERSION` for bigger changes (`2.1`, `3.0`); the patch part is automatic.

## Releases / CI
Every push to `main` (except docs-only changes) runs a 3-way matrix (x86, x64, arm64): publish, build the installer,
upload. A final job waits for **all three**, writes `SHA256SUMS.txt`, and only then creates the Release, so the
self-updater never sees a half-uploaded release. Inno Setup is pinned to `INNO_VERSION` (7.1.0) at the top of
`.github/workflows/build.yml` instead of whatever the runner image ships.

## Installer
Dark, per-user (no admin), no folder/shortcut questions. Running a newer installer over an older
install says it's an **update** (`2.0.3 to 2.0.4`), replaces the program files cleanly, and keeps your
settings. Uninstalling asks whether to delete the updater's saved settings (default: keep).
Silent mode for scripts: `VlcNightlyUpdater-Setup-X.Y.Z-x64.exe /VERYSILENT` (add `/relaunch=1` to reopen the app
afterwards, which is what self-update does).
