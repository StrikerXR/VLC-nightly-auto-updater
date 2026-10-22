# VLC Nightly Updater

Tiny WinUI 3 app (Windows 11, x64 only). Open it, it checks
https://artifacts.videolan.org/vlc/nightly-win64/ and, if there's a newer build than the one it
last recorded, downloads the `.exe`, verifies SHA512, and installs it silently (`/S`) over your
existing VLC. User config in `%APPDATA%\vlc` is never touched (no uninstall step).

- First run with VLC already installed: assumes you're on the latest and just records it.
- State file: `%LOCALAPPDATA%\VlcNightlyUpdater\state.json` (delete it to reset).
- Nothing runs in the background.

## Build
Push to GitHub; Actions builds it. Grab the zip from the run's Artifacts, or tag `v1.0.0`
to get it attached to a Release. Unzip anywhere and run `VlcNightlyUpdater.exe`.

Local: `dotnet publish VlcNightlyUpdater/VlcNightlyUpdater.csproj -c Release -r win-x64 -p:Platform=x64 -o publish`
