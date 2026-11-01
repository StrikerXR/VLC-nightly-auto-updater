# AGENTS.md — read this first, it is the base for every change

WinUI 3 app (.NET 8, Windows 10 1809+, x64 + ARM64) that keeps VLC's nightly build current.
This file is deliberately short. Open only the file you need (see the map); never read the whole repo.

## 1. Hard rules (never break)
* **Workflows:** do not modify `.github/workflows/` unless explicitly told to.
* **Dependencies:** do not upgrade, downgrade or re-pin existing packages unless told to.
* **New packages:** look up the exact latest version first. Never guess a version string.
* **Never touch** `%APPDATA%\vlc` (the user's VLC settings) from code.
* **Installer sync:** names in `Config/AppConfig.cs` marked "keep in sync with installer.iss" must match `installer/installer.iss`.
* **Version:** the single source is `VERSION`. Do not bump it unless asked.

## 2. Where to change things (the map)
| I want to…                                   | Edit only this                              |
|----------------------------------------------|---------------------------------------------|
| Change a URL, size, count, timeout, task name | `VlcNightlyUpdater/Config/AppConfig.cs`    |
| Reword a status line, dialog or note          | `Config/Strings.cs`                         |
| Change a color, radius or opacity             | `Theme/Tokens.xaml`                         |
| Change a shared look (card, label, mono)      | `Theme/Styles.xaml`                         |
| Change a glyph used in code                   | `Config/Icons.cs`                           |
| Change how dates/sizes/times display          | `Config/Format.cs`                          |
| Add or change a setting                       | `Services/AppState.cs` + `Views/SettingsPanel.xaml(.cs)` |
| Change the status card                        | `Views/StatusCard.xaml(.cs)`                |
| Change the 3 tiles                            | `Views/BuildTiles.xaml(.cs)`, `Views/Tile.xaml(.cs)` |
| Change the commits list                       | `Views/ChangesPanel.xaml(.cs)` + `MainWindow.Changes.cs` |
| Change the Builds tab (rollback, cache)       | `Views/BuildsPanel.xaml(.cs)`               |
| Change bottom buttons                         | `Views/ActionBar.xaml(.cs)`                 |
| Add a tab                                     | `MainWindow.xaml` (SelectorBarItem + panel) + one line in `Tabs_SelectionChanged` |
| Change the check / install / self-update flow | `MainWindow.Check.cs` / `.Install.cs` / `.SelfUpdate.cs` |
| Change window look, size, icon choice         | `MainWindow.Chrome.cs`                      |
| Change VLC channel discovery / download       | `Services/UpdateService.cs`, `Downloader.cs`|
| Change updater self-update (incl. pre-releases) | `Services/SelfUpdater.cs`                 |
| Change the background notification            | `Services/BackgroundCheck.cs`               |
| Redraw the app icon                           | `tools/make_icons.py`, then run it          |

## 3. Structure rules (keep it cheap for the next agent)
* **Small files.** Aim for under ~150 lines, hard cap ~250. One topic per file. If a file grows, split it (partial class for `MainWindow`, new `UserControl` for UI).
* **Views are dumb.** A view exposes a few public methods/events (`Set…`, `Show…`, `…Clicked`). It never reaches into another view's controls. `MainWindow` is the only place that connects views.
* **No logic in XAML code-behind beyond wiring.** Network, files, registry, processes live in `Services/`.
* **No magic values inline.** URLs and numbers → `AppConfig`. Runtime messages → `Strings`. Colors → `Tokens.xaml`. Static labels (button text, tab names, setting titles) stay in the `.xaml` next to the control.
* **Services have no UI.** They take a `CancellationToken` and `IProgress<T>`; they never show dialogs.
* **Every file starts with a `///` or `<!-- -->` header** saying what it is and how to extend it. Keep it true when you edit.
* **Settings:** add a property with a default to `AppState` (old state.json files keep working). Prefer fewer settings; justify each new one.
* **Errors:** user-facing failures go through `Status.Set(..., Phase.Bad)` via `RunGuardedAsync`; don't add new try/catch dialogs.
* Use `ImplicitUsings`; `Nullable` is on. No new warnings.

## 3b. Verify
* Build: `dotnet build VlcNightlyUpdater -c Release -p:Platform=x64` (needs the Windows SDK; Windows only).
* If you cannot build, say so; do not claim it compiles. Check XAML is well-formed and every `x:Name`/handler you used exists.
* Smoke test by eye: opens, checks, tiles fill, all 3 tabs switch, Settings toggles persist across restart.

## 4. Design language (follow it for every pixel)
**Feel:** calm, dark-friendly Windows 11 utility. Native controls, one warm accent, generous space, no decoration that doesn't carry information.

**Color** (all in `Theme/Tokens.xaml`; never hard-code)
* Brand orange `#FF8800` = the only accent: progress bar, working/download icon, title badge, primary button (system accent button).
* Phases: working/download = orange, good = green `#6CCB5F`, warn = yellow `#FCE100`, bad = red `#FF6B6B`. The badge behind the icon uses the same color at 20% (`…TintBrush`).
* Surfaces come from Windows theme resources (`CardBackgroundFillColorDefaultBrush`, etc.) so light/dark and Mica just work. Don't paint backgrounds.

**Shape & space**
* Cards: `VnuCard` (radius 12, 1px card stroke, padding 16; status card 20; tiles 16,12).
* Page padding 24 sides / 20 bottom; 12 between blocks; 8 between buttons; 10–14 inside cards.
* Window 680×800 DIP, resizable, not maximizable; never taller than the screen.

**Type**
* Headline: `SubtitleTextBlockStyle`. Body: default. Meta/hints: `VnuLabel` (12px, 60% opacity). Build numbers: `VnuMono` (Consolas 16).
* Section labels in tiles are UPPERCASE `VnuLabel`; everything else is sentence case. Use `…` (not `...`) and `→` for "from → to". Join meta with ` · ` (`Format.Join`).

**Layout**
* Top to bottom: banner (only when needed) → status card → tiles → tabs (Changes · Settings · Builds) → active panel (fills space, scrolls inside) → action bar.
* One primary (accent) button on screen: "Check again" / "Update now". Secondary buttons are default style. Icon buttons carry a tooltip.
* Settings are plain toggles with a short title; add a one-line hint only when the consequence isn't obvious. Hide controls that don't apply (e.g. build picker on x64 PCs).

**Voice**
* Short, calm, plain English. Say what happened and what to do next. No exclamation marks, no jargon, no blame. Errors show the real reason in the detail line.

**Motion:** none beyond built-in control transitions. Progress is a bar plus a text line (`12 MB of 40 MB · 6 MB/s · 8s left`).

**Icons:** Segoe Fluent Icons glyphs only (`FontIcon`), 14px in buttons, 24px in the status badge. The app icon is generated by `tools/make_icons.py`: orange squircle with top-left light, white rounded arrow into a tray, small crescent moon ("nightly") at ≥64px; December swaps in a Santa-hat variant. Keep file names `app.ico` / `app-christmas.ico` (CI and installer reference them).

**Accessibility:** every interactive control is reachable by keyboard and has visible text or a tooltip; never convey state by color alone (the status icon glyph changes with the phase too).
