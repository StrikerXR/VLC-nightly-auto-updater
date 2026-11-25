# What's new in 5.1.0

Safer updates and a few comforts: go back with one click, let VLC finish before it updates, and a first-run question so the updater never has to guess.

## New
- **Undo last update.** Updated and something broke? Press the undo button at the bottom of the window to go back to the build you had before. It uses the installer saved on your PC when there is one (so it works offline), and downloads that build again when there isn't. The build you backed out of is skipped, so it isn't installed again right away. A newer nightly, if there is one, is still installed as usual.
- **Install when VLC closes.** If VLC is open during an update, you can now choose **Wait until VLC closes**. The installer is downloaded and checked first, then the update starts as soon as you close VLC. Nothing is closed for you, and you can press Cancel at any time. Prefer it every time? Turn on **Wait for VLC instead of closing it** in Settings.
- **Is this the latest nightly?** The first time the updater sees your VLC, it asks. **Yes** keeps your build as it is. **No** installs the newest nightly. **I don't know** also installs the newest nightly, to be safe. Closing the question counts as I don't know. Your VLC settings are kept either way.
- **Buttons on the notification.** The new-nightly notification now has **Update now** and **Skip this build**. Skip works without opening the window. Update now opens the updater and starts the install, and it still asks before closing VLC.
- **Filter the commits.** The Changes tab has a search box and a **Hide merges and translations** option. Searching looks at the title, the author and the commit ID.
- **Copy diagnostics.** A link in Settings copies the versions, your build, your settings and the current status to the clipboard, ready to paste into a bug report. Your user folder is shortened, and no VLC settings are included.
- **Installers to keep.** Choose in Settings how many installers stay on disk for offline rollback: 2, 3 (the default), 5 or 10.

## Improved
- **The "VLC is running" question** has a third choice, Wait until VLC closes, next to Close VLC and update and Cancel.
- **Undo always has something to go back to.** The installer of the build you just replaced is never cleaned up, even with the smallest cache setting.

## Fixed
- **Skipped builds stay quiet.** If you had skipped a nightly, the background check could still show a notification about it. It no longer does.

## Good to know
- Undo only goes back to a build of the same CPU type, so it can never switch between 64-bit and ARM64 by accident. After an update from 5.0.0, undo appears once the updater has your previous build on record. For updates made before 5.1.0, it appears only if that build's installer is still saved on your PC.
- After **Wait until VLC closes**, VLC is not reopened, because you closed it yourself. Turn on **Open VLC after updating** if you want it back every time.
- The notification buttons need **Notify me about new nightlies** to be on, and they only work in the installed copy. If the updater is already open when you press one, it just comes to the front and nothing else happens.
- The Hide merges and translations option is a best guess from the commit title and author. It can miss one or hide an odd one, and it never changes the number of commits behind.
- If you already use the updater, you won't be asked the first-run question. It is only for a fresh start.
