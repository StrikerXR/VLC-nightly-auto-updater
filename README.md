# VLC Nightly Updater

Small WinUI 3 app for **Windows 10 (1809 and newer) and Windows 11**, built for **64-bit and ARM64** only. Open it and it
checks VideoLAN's nightly channel for your CPU type; if there's a newer build than the one it last recorded, it
downloads the `.exe`, verifies SHA512, and installs it silently (`/S`) over your existing VLC. `%APPDATA%\vlc` is
never touched (no uninstall step).

**Nothing runs in the background by default**: no startup entry, no services, no scheduled tasks. There is one
optional exception, described under "Background update checks", and it is off until you turn it on.

## For contributors and AI agents
Start with [AGENTS.md](AGENTS.md): hard rules, a "change X → edit file Y" map, structure rules and the design language.
The UI is split into small views in `VlcNightlyUpdater/Views/`, flows in `MainWindow.*.cs`, and all URLs/text/colors/icons
each live in one file (`Config/`, `Theme/`). `python tools/make_icons.py` regenerates the app icons.

## Features
- **Undo last update** (new in 5.1): one button goes back to the build you had before, from the cached installer (works offline)
  or by downloading it again. The build you backed out of is skipped. Same CPU type only
- **Install when VLC closes** (new in 5.1): if VLC is open, choose *Wait until VLC closes* (or turn on *Wait for VLC instead of
  closing it*). Nothing is closed for you; VLC isn't reopened afterwards unless *Open VLC after updating* is on
- **First-run question** (new in 5.1): *Is this the latest nightly?* Yes keeps it; No and *I don't know* install the newest (safe default)
- **Notification buttons** (new in 5.1): *Update now* and *Skip this build* on the new-nightly notification (see "Background update checks")
- **Changes filter** (new in 5.1): search the commits and optionally hide merges and translations
- **Copy diagnostics** (new in 5.1): Settings puts a short report on the clipboard for bug reports
- **Installers to keep** (new in 5.1): 2, 3 (default), 5 or 10 cached installers
- **Release-notes dialog** (since 5.0): a new updater version shows its full notes first, then *Update and restart* or
  *Later*; after an update the notes show once more. The same text is the top of the GitHub release (from `CHANGELOG.md`).
  Updates from 5.0.0 onward show it; older versions don't contain this feature
- **Portable version** (since 5.0): a zip, no installer. See "Portable" below
- **Skip this build**: don't install a broken nightly; the updater waits for the next one
- **Recent installs**: the Builds tab lists your last installs with dates; **F5** checks again
- **Commits since your build**: the Changes tab lists the VLC commits between your build and the latest, loads by itself, and a
  *Behind* tile shows how many commits you're missing
- **Pre-releases for the updater** (Settings > Include pre-releases): checks GitHub's release list and takes whichever updater
  version is newest, pre-release or not
- **Simple settings**: update automatically, open VLC afterwards, wait for VLC instead of closing it, installers to keep, keep the updater current, include pre-releases, notifications, copy diagnostics.
  VLC is always reopened if it was running; the build picker shows only on ARM64 PCs
- **Auto-update on open** (or switch to a manual "Update now" button)
- **Updates itself** from this repo's GitHub Releases (see below)
- **Optional background update checks** (off by default): a notification when a new nightly is out
- **Windows 10 and 11**: Mica on Windows 11, an acrylic/plain backdrop on Windows 10, same app everywhere
- **Santa hat in December**: the window/taskbar icon gets a Christmas version for the month of December
- **64-bit and ARM64**: the updater is built for both, and it manages whichever VLC you have (32-bit VLC is not supported)
- **Resilient downloads**: resumes after a dropped connection or a stall (HTTP Range), retries with backoff,
  shows speed and time left, has a Cancel button, and a half-finished download resumes next time
- **What's new**: lists the VLC commits between your build and the latest, and what your last update brought
  (from code.videolan.org; click a commit to open it)
- **Smarter install flow**: downloads while VLC keeps running, closes VLC only right before installing
  (gently, force-close only if you agree) or waits until you close it, reopens it afterwards (not after a wait), retries a failed install once, checks free disk
  space, and confirms vlc.exe really landed as the right architecture
- **Offline rollback**: the last few verified installers are cached (3 by default, 2/3/5/10 in Settings); cached builds are marked in the picker.
  The installers of the current and the previous build are never cleaned up, so Undo works offline
- First run with VLC installed: asks *Is this the latest nightly?* (Yes records it; No / I don't know install the newest)
- Installed / latest build tiles with readable dates and CPU type
- Builds tab: install any of the 15 most recent builds (rollback or reinstall)
- Optional: launch VLC after updating
- One-click button to open the VLC settings folder

State + options: `%LOCALAPPDATA%\VlcNightlyUpdater\state.json` (portable: the `Data` folder next to the exe). Cached installers: `...\VlcNightlyUpdater\cache\<arch>`.

## Portable
Each release also ships `VlcNightlyUpdater-Portable-X.Y.Z-<arch>.zip`. Unzip it anywhere and run `VlcNightlyUpdater.exe`.
A file named `portable.flag` next to the exe is what makes a copy portable: everything it saves (settings, cached installers)
goes to a `Data` folder beside the exe, so deleting the folder removes every trace. A portable copy never installs itself over
anything, never creates the background task or registry keys, and when a new version exists it shows the release notes and
opens the download page. It still updates VLC itself exactly like the installed copy. Delete `portable.flag` to turn it into a normal copy.

## Background update checks (optional, off by default)
Settings > "Notify me about new nightlies". It only works in the installed copy.

- **Off (default):** nothing is registered with Windows and nothing runs. Each time the app opens with the option off,
  it also deletes any leftover task/keys, so the switch is always the source of truth.
- **On:** the app creates one per-user scheduled task (`VlcNightlyUpdater Background Check`, no admin needed) that
  starts `VlcNightlyUpdater.exe --background-check` at sign-in and every 6 hours. That run has no window: it asks
  VideoLAN for the newest nightly for your CPU type, shows a Windows notification if it is newer than your installed
  build (once per build), and exits. It **never installs anything**; click the notification to open the app.
  It also adds two small per-user registry keys so Windows can show the notification and open the app when you click it
  (`HKCU\Software\Classes\AppUserModelId\StrikerXR.VlcNightlyUpdater` and `HKCU\Software\Classes\vlcnightlyupdater`).
- **Notification buttons (5.1):** the nightly notification has *Update now* (opens the app and starts the install; it still asks
  before closing VLC and never switches CPU type without asking) and *Skip this build* (recorded without opening a window, then a
  small confirmation notification). They start the exe with `vlcnightlyupdater:update` / `vlcnightlyupdater:skip?build=<folder>`.
  If the app is already open, a button only brings it to the front. Skipped builds are never notified about.
- Turning it off, or uninstalling, removes the task and those keys.

## Architectures
VLC publishes a separate nightly channel per CPU type, and the updater picks the right one:

| VLC build | Channel (`https://artifacts.videolan.org/vlc/...`) |
|-----------|-----------------------------------------------------|
| 64-bit    | `nightly-win64/` |
| ARM64     | `nightly-win64-arm-llvm/` |

"Auto" (the default) matches whatever VLC you already have, detected from `vlc.exe` itself. Otherwise it uses
your Windows CPU type. On ARM64 PCs you can pick a different build in Settings; switching architecture always asks first, even
with auto-update on. If VideoLAN renames a channel, edit `Channels` in `Config/AppConfig.cs`.

The updater itself ships as two setups (`-x64`, `-arm64`). 64-bit is primary; the ARM64 build is best-effort, and
if it fails in CI the release goes out with x64 only.

## Self-update
On open, the app asks `https://api.github.com/repos/StrikerXR/VLC-nightly-auto-updater/releases/latest`. If the tag
is newer than the running version, it downloads the setup that matches **its own** CPU type, checks it against the
release's `SHA256SUMS.txt`, runs it silently, and reopens itself. If the checksum file is missing, nothing is
installed. It only self-updates the installed copy (never a build-folder, portable or debugger run), and a failed self-update
never blocks the VLC update. Turn it off in Settings ("Keep this updater up to date"), and you get
an "Update now" banner instead.

Updating from 1.x: 1.x can't self-update, so install a 2.0 setup once by hand. It upgrades the old install in place.

## Versioning (one version for everything)
`VERSION` holds the full version (currently `5.1.0`) and that exact number is used for the app, the installers, the
exe names (`VlcNightlyUpdater-Setup-5.1.0-x64.exe`), the git tag (`v5.1.0`) and the GitHub Release. An installed
`5.0.x` sees `5.1.0` as newer and updates itself. To ship the next version, edit `VERSION` (e.g. `5.1.1`) and run
the workflow. Re-running the workflow for the same version just replaces that release's files.

## Releases / CI
`.github/workflows/build.yml` does three things:

- **It only runs when you start it** (Actions > Build & Release > Run workflow); pushes and PRs don't trigger it. Options:
  publish or dry run, include ARM64, pre-release, draft, and extra release notes. A dry run (untick "release") builds both CPU
  types and smoke-tests the x64 installer (install, reinstall over the top, uninstall, confirm nothing is left behind) and
  publishes nothing.
- **Safety checks along the way**: `VERSION` must be `MAJOR.MINOR.PATCH` and not older than the latest release; the
  published exe's PE header must match the CPU type being built; the release job refuses to run without the x64 installer
  (ARM64 stays best-effort); after publishing, the workflow downloads the release and checks every file against
  `SHA256SUMS.txt`, the same way the in-app self-updater does.

**Release notes:** `CHANGELOG.md` holds only the notes of the version being released. CI refuses to release without it,
puts it at the top of the GitHub release, and the app shows it in the "what's new" dialog. It is overwritten for every release.

Other details: installers are built by whatever Inno Setup the runner ships (no pinning), NuGet packages are cached,
build provenance is attested on public repos, each run writes a summary (versions, sizes, hashes, release link), and
Dependabot keeps the Actions versions and NuGet packages current (Windows App SDK major/minor bumps stay manual, since
they can change the minimum Windows version).

## Installer
Dark, per-user (no admin), no folder/shortcut questions, Windows 10 1809+ and Windows 11. Running a newer installer over an older
install says it's an **update** (`5.0.0 to 5.1.0`), replaces the program files cleanly, and keeps your
settings. **Uninstalling removes everything** and asks nothing: the program files, saved settings and cached
installers (`%LOCALAPPDATA%\VlcNightlyUpdater`), self-update downloads in `%TEMP%`, the background-check task and
registry keys, and the Start menu entry. Your VLC settings (`%APPDATA%\vlc`) and VLC itself are never touched.
The installer writes no setup log, so it leaves no file in `%TEMP%` either.
Silent mode for scripts: `VlcNightlyUpdater-Setup-X.Y.Z-x64.exe /VERYSILENT` (add `/relaunch=1` to reopen the app
afterwards, which is what self-update does).
