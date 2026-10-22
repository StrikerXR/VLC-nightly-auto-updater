# VLC Nightly Updater

Small WinUI 3 app for **Windows 11 25H2 or newer**, built for **64-bit and ARM64** only. Open it and it
checks VideoLAN's nightly channel for your CPU type; if there's a newer build than the one it last recorded, it
downloads the `.exe`, verifies SHA512, and installs it silently (`/S`) over your existing VLC. `%APPDATA%\vlc` is
never touched (no uninstall step).

**Nothing runs in the background by default**: no startup entry, no services, no scheduled tasks. There is one
optional exception, described under "Background update checks", and it is off until you turn it on.

## Features
- **Auto-update on open** (or switch to a manual "Update now" button)
- **Updates itself** from this repo's GitHub Releases (see below)
- **Optional background update checks** (off by default): a notification when a new nightly is out
- **Santa hat in December**: the window/taskbar icon gets a Christmas version for the month of December
- **64-bit and ARM64**: the updater is built for both, and it manages whichever VLC you have (32-bit VLC is not supported)
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

## Background update checks (optional, off by default)
Options > "Check for VLC updates in the background". It only works in the installed copy.

- **Off (default):** nothing is registered with Windows and nothing runs. Each time the app opens with the option off,
  it also deletes any leftover task/keys, so the switch is always the source of truth.
- **On:** the app creates one per-user scheduled task (`VlcNightlyUpdater Background Check`, no admin needed) that
  starts `VlcNightlyUpdater.exe --background-check` at sign-in and every 6 hours. That run has no window: it asks
  VideoLAN for the newest nightly for your CPU type, shows a Windows notification if it is newer than your installed
  build (once per build), and exits. It **never installs anything**; click the notification to open the app.
  It also adds two small per-user registry keys so Windows can show the notification and open the app when you click it
  (`HKCU\Software\Classes\AppUserModelId\StrikerXR.VlcNightlyUpdater` and `HKCU\Software\Classes\vlcnightlyupdater`).
- Turning it off, or uninstalling, removes the task and those keys.

## Architectures
VLC publishes a separate nightly channel per CPU type, and the updater picks the right one:

| VLC build | Channel (`https://artifacts.videolan.org/vlc/...`) |
|-----------|-----------------------------------------------------|
| 64-bit    | `nightly-win64/` |
| ARM64     | `nightly-win64-arm-llvm/` |

"Auto" (the default) matches whatever VLC you already have, detected from `vlc.exe` itself. Otherwise it uses
your Windows CPU type. You can pick a different build in Options; switching architecture always asks first, even
with auto-update on. If VideoLAN renames a channel, edit the `Channels` table in `Services/UpdateService.cs`.

The updater itself ships as two setups (`-x64`, `-arm64`). 64-bit is primary; the ARM64 build is best-effort, and
if it fails in CI the release goes out with x64 only.

## Self-update
On open, the app asks `https://api.github.com/repos/StrikerXR/VLC-nightly-auto-updater/releases/latest`. If the tag
is newer than the running version, it downloads the setup that matches **its own** CPU type, checks it against the
release's `SHA256SUMS.txt`, runs it silently, and reopens itself. If the checksum file is missing, nothing is
installed. It only self-updates the installed copy (never a build-folder or debugger run), and a failed self-update
never blocks the VLC update. Turn it off in Options ("Keep this updater up to date automatically"), and you get
an "Update now" banner instead.

Updating from 1.x: 1.x can't self-update, so install a 2.0 setup once by hand. It upgrades the old install in place.

## Versioning (one version for everything)
`VERSION` holds the full version (currently `2.1.0`) and that exact number is used for the app, the installers, the
exe names (`VlcNightlyUpdater-Setup-2.1.0-x64.exe`), the git tag (`v2.1.0`) and the GitHub Release. An installed
`2.0.14` sees `2.1.0` as newer and updates itself. To ship the next version, edit `VERSION` (e.g. `2.1.1`) and run
the workflow. Re-running the workflow for the same version just replaces that release's files.

## Releases / CI
Running the **Build & Release** workflow (manually, from the Actions tab) runs a 2-way matrix (x64, arm64): publish, build the installer,
upload. A final job waits for the builds (x64 is required, ARM64 is optional), writes `SHA256SUMS.txt`, and only then creates the Release, so the
self-updater never sees a half-uploaded release. The installer is compiled with whatever Inno Setup the runner image ships (no pinning).

## Installer
Dark, per-user (no admin), no folder/shortcut questions, Windows 11 25H2+ only. Running a newer installer over an older
install says it's an **update** (`2.0.14 to 2.1.0`), replaces the program files cleanly, and keeps your
settings. **Uninstalling removes everything** and asks nothing: the program files, saved settings and cached
installers (`%LOCALAPPDATA%\VlcNightlyUpdater`), self-update downloads in `%TEMP%`, the background-check task and
registry keys, and the Start menu entry. Your VLC settings (`%APPDATA%\vlc`) and VLC itself are never touched.
The installer writes no setup log, so it leaves no file in `%TEMP%` either.
Silent mode for scripts: `VlcNightlyUpdater-Setup-X.Y.Z-x64.exe /VERYSILENT` (add `/relaunch=1` to reopen the app
afterwards, which is what self-update does).
