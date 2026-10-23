using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VlcNightlyUpdater.Services;
using Windows.Graphics;
using Windows.UI;

namespace VlcNightlyUpdater;

public sealed partial class MainWindow : Window
{
    private enum Phase { Working, Download, Good, Warn, Bad }
    private sealed record BuildItem(string Folder, string Label);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // Swallows "maximize" however it's asked for (title-bar double-click, Win+Up, system menu).
    private delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);
    private SubclassProc? _subclass;   // kept in a field so the GC can't free the callback
    private nint _hwnd;

    private static readonly Dictionary<string, string> SummaryCache = [];

    private static readonly Color Orange = Color.FromArgb(255, 255, 136, 0);
    private static readonly Color Green = Color.FromArgb(255, 108, 203, 95);
    private static readonly Color Amber = Color.FromArgb(255, 252, 225, 0);
    private static readonly Color Red = Color.FromArgb(255, 255, 107, 107);

    private readonly UpdateService _updates = new();
    private readonly CancellationTokenSource _cts = new();   // the window: cancelled when it closes
    private CancellationTokenSource? _opCts;                  // one action: cancelled by the Cancel button too
    private readonly AppState _state = StateStore.Load();

    private BuildInfo? _pending;        // an update that's waiting for the user to press "Update now"
    private BuildInfo? _latest;         // newest build found by the last check
    private SelfUpdateInfo? _selfUpdate;
    private List<string> _folders = [];
    private VlcArch _target = ArchInfo.Native;
    private bool _busy, _started, _closed, _loadingUi;
    private Phase? _lastPhase;

    private CancellationToken Ct => _opCts?.Token ?? _cts.Token;

    public MainWindow()
    {
        InitializeComponent();
        SetupWindow();

        VersionText.Text = $"v{Net.CurrentVersion}" + (IsDecember ? " \U0001F384" : "");

        _loadingUi = true;
        AutoToggle.IsOn = _state.AutoUpdate;
        LaunchToggle.IsOn = _state.LaunchAfterUpdate;
        CloseToggle.IsOn = _state.CloseAfterUpdate;
        ReopenToggle.IsOn = _state.ReopenIfWasRunning;
        SelfToggle.IsOn = _state.AutoUpdateSelf;
        BackgroundToggle.IsOn = _state.BackgroundCheck;
        AiToggle.IsOn = _state.AiSummaryEnabled;
        AiModelBox.Text = string.IsNullOrWhiteSpace(_state.GeminiModel) ? AiSummaryService.DefaultModel : _state.GeminiModel;
        if (AiSummaryService.Unprotect(_state.GeminiKeyProtected) is { } k) AiKeyBox.Password = k;
        PauseDate.MinDate = new DateTimeOffset(DateTime.Today.AddDays(1));
        PauseDate.MaxDate = new DateTimeOffset(DateTime.Today.AddDays(365));
        if (_state.IsPaused && _state.PausedUntil!.Value.Date >= PauseDate.MinDate.Date)
            PauseDate.Date = new DateTimeOffset(_state.PausedUntil.Value.Date);
        BuildArchPicker();
        _loadingUi = false;
        RefreshAiPanel();
        RefreshPauseUi();
        BackgroundLast.Text = _state.LastBackgroundRun is { } lr ? $"Last background check: {lr}" : "No background check has run yet.";
        SyncBackgroundCheck();

        SelfUpdater.CleanupStale();
        RefreshCacheText();
        SetTiles(_state.InstalledBuild, null, ArchInfo.ParseKey(_state.InstalledArch));
        RefreshLastChecked();

        Closed += (_, _) =>
        {
            _closed = true; _cts.Cancel();
            SaveAiFields();
            if (_subclass is not null) RemoveWindowSubclass(_hwnd, _subclass, 1);
        };
        Root.Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;

            // Staggered entrance.
            Motion.Reveal(StatusCard, 0);
            Motion.Reveal(TilesGrid, 70);
            Motion.Reveal(OptionsScroll, 140);
            Motion.Reveal(ActionsRow, 210);

            await RunGuardedAsync(CheckFlowAsync);
        };
    }

    private static bool IsDecember => DateTime.Now.Month == 12;

    // ---------- optional background check ----------

    /// <summary>
    /// Makes Windows match the saved switch. OFF: make sure no scheduled task or registry key is left behind.
    /// ON: refresh them (the install folder may have changed). Runs off the UI thread.
    /// </summary>
    private void SyncBackgroundCheck()
    {
        BackgroundNote.Text = BackgroundNoteText(_state.BackgroundCheck);
        TestNotifyButton.Visibility = _state.BackgroundCheck ? Visibility.Visible : Visibility.Collapsed;

        // Only the installed copy may refresh the task on startup: a copy run from a build folder must not
        // re-point it at a path that goes away. (Turning the switch on by hand still works from anywhere.)
        if (!SelfUpdater.IsInstalledCopy()) return;

        var on = _state.BackgroundCheck;
        _ = Task.Run(() =>
        {
            try { if (on) BackgroundCheck.Enable(); else BackgroundCheck.Disable(); }
            catch { /* best-effort refresh; the toggle itself reports errors */ }
        });
    }

    private static string BackgroundNoteText(bool on)
    {
        var where = SelfUpdater.IsInstalledCopy() ? "" : $" It will run this copy ({AppContext.BaseDirectory}), so don't move or delete it.";
        return on
            ? "On: a scheduled task checks at sign-in and every 6 hours and shows a notification. It never installs by itself." + where
            : "Off: nothing runs in the background. Turn on to get a notification when a new nightly is out." + where;
    }

    private async void TestNotifyButton_Click(object sender, RoutedEventArgs e)
    {
        TestNotifyButton.IsEnabled = false;
        var ok = await Task.Run(BackgroundCheck.SendTest);
        TestNotifyButton.IsEnabled = true;
        if (!ok)
            await ShowMessageAsync("Windows blocked the notification",
                "Open Settings > System > Notifications, make sure notifications are on (and Do Not Disturb is off), " +
                "then find \"VLC Nightly Updater\" in the app list and allow it.");
    }

    private async void BackgroundToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        var on = BackgroundToggle.IsOn;
        BackgroundToggle.IsEnabled = false;
        try
        {
            await Task.Run(() => { if (on) BackgroundCheck.Enable(); else BackgroundCheck.Disable(); });
            _state.BackgroundCheck = on;
            if (!on) _state.LastNotifiedBuild = null;
            StateStore.Save(_state);
            BackgroundNote.Text = BackgroundNoteText(on);
            TestNotifyButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

            // Prove it works right away instead of leaving the user guessing.
            if (on && !await Task.Run(BackgroundCheck.SendTest))
                await ShowMessageAsync("Background checks are on, but Windows blocked notifications",
                    "Open Settings > System > Notifications, turn notifications on (and Do Not Disturb off), " +
                    "then allow \"VLC Nightly Updater\".");
        }
        catch (Exception ex)
        {
            _loadingUi = true;
            BackgroundToggle.IsOn = !on;
            _loadingUi = false;
            await ShowMessageAsync(on ? "Couldn't turn on background checks" : "Couldn't turn off background checks", ex.Message);
        }
        finally
        {
            BackgroundToggle.IsEnabled = true;
        }
    }

    // ---------- window chrome ----------

    private void SetupWindow()
    {
        // No classic title bar: our own header is the drag region, only the native min/close buttons remain.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
        }

        // The disabled maximize button isn't enough: a double-click on our custom title area still maximized.
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _subclass = (hWnd, msg, wParam, lParam, id, data) =>
        {
            const uint WM_SYSCOMMAND = 0x0112, WM_NCLBUTTONDBLCLK = 0x00A3;
            const int HTCAPTION = 2, SC_MAXIMIZE = 0xF030;
            if (msg == WM_NCLBUTTONDBLCLK && (int)wParam == HTCAPTION) return 0;
            if (msg == WM_SYSCOMMAND && ((int)wParam & 0xFFF0) == SC_MAXIMIZE) return 0;
            return DefSubclassProc(hWnd, msg, wParam, lParam);
        };
        SetWindowSubclass(_hwnd, _subclass, 1, 0);
        // Belt and braces: if anything still maximizes us, snap straight back.
        AppWindow.Changed += (_, a) =>
        {
            if (a.DidPresenterChange && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } op)
                op.Restore();
        };

        // December: the window and taskbar icon wears a Santa hat. Any other month: the normal icon.
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var icon = Path.Combine(assets, IsDecember ? "app-christmas.ico" : "app.ico");
        if (!File.Exists(icon)) icon = Path.Combine(assets, "app.ico");
        try { AppWindow.SetIcon(icon); } catch { }

        // Size in DIPs -> pixels, then center on the current display.
        var scale = GetDpiForWindow(_hwnd) / 96.0;
        int w = (int)(540 * scale), h = (int)(740 * scale);
        AppWindow.Resize(new SizeInt32(w, h));

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new PointInt32(
            area.X + (area.Width - w) / 2,
            area.Y + (area.Height - h) / 2));
    }

    // ---------- UI helpers ----------

    private static string Pretty(string? folder) =>
        folder is not null &&
        DateTime.TryParseExact(folder, "yyyyMMdd-HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMM d, yyyy \u00B7 HH:mm", CultureInfo.InvariantCulture)
            : "";

    private static string Bytes(double b) =>
        b >= 1024 * 1024 * 1024 ? $"{b / 1024 / 1024 / 1024:0.0} GB"
        : b >= 1024 * 1024 ? $"{b / 1024 / 1024:0.0} MB"
        : b >= 1024 ? $"{b / 1024:0} KB"
        : $"{b:0} B";

    private static string FormatEta(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s"
        : $"{Math.Max(1, (int)t.TotalSeconds)}s";

    private void SetTiles(string? installed, BuildInfo? latest, VlcArch? installedArch)
    {
        SetAnimated(InstalledText, installed ?? "\u2014");
        InstalledDate.Text = Join(Pretty(installed), installedArch?.Label());
        if (latest is not null)
        {
            SetAnimated(LatestText, latest.Folder);
            LatestDate.Text = Join(Pretty(latest.Folder), latest.Arch.Label());
        }

        static void SetAnimated(TextBlock tb, string text)
        {
            if (tb.Text == text) return;
            tb.Text = text;
            Motion.Swap(tb);
        }

        static string Join(string a, string? b) =>
            string.IsNullOrEmpty(b) ? a : string.IsNullOrEmpty(a) ? b : $"{a} \u00B7 {b}";
    }

    private void RefreshLastChecked() =>
        LastCheckedText.Text = _state.LastChecked is { } t ? $"Last checked {t:t}" : "";

    private void RefreshActions()
    {
        var update = _pending is not null;
        PrimaryText.Text = update ? (_state.OnStable ? "Back to nightly" : "Update now") : "Check again";
        PrimaryIcon.Glyph = update ? "\uE896" : "\uE72C";
        UpdateBadge.Visibility = update && !_state.OnStable ? Visibility.Visible : Visibility.Collapsed;
        GoStableButton.Visibility = _state.OnStable ? Visibility.Collapsed : Visibility.Visible;
        RefreshChangesButton();
    }

    private void RefreshCacheText()
    {
        var (count, bytes) = InstallerCache.Stats();
        CacheText.Text = count == 0
            ? "No cached installers"
            : $"{count} cached installer{(count == 1 ? "" : "s")} \u00B7 {Bytes(bytes)} (for offline rollback)";
        CacheBar.Maximum = InstallerCache.Keep;
        CacheBar.Value = Math.Min(count, InstallerCache.Keep);
        ClearCacheButton.IsEnabled = count > 0 && !_busy;
    }

    private void FillBuildPicker() =>
        BuildPicker.ItemsSource = _folders.Take(15).Select(f =>
        {
            var label = Pretty(f) is { Length: > 0 } p ? $"{f}   \u00B7   {p}" : f;
            if (InstallerCache.Has(f, _target)) label += "   \u00B7   cached";
            return new BuildItem(f, label);
        }).ToList();

    private void BuildArchPicker()
    {
        ArchPicker.Items.Clear();
        ArchPicker.Items.Add(new ComboBoxItem { Content = "Auto (match the installed VLC)", Tag = "auto" });
        foreach (var a in ArchInfo.Supported)
            ArchPicker.Items.Add(new ComboBoxItem { Content = a.Label(), Tag = a.Key() });

        var want = ArchInfo.ParseKey(_state.ArchOverride)?.Key() ?? "auto";
        ArchPicker.SelectedItem = ArchPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == want)
                                  ?? ArchPicker.Items[0];
    }

    private void SetStatus(string title, string? detail, Phase phase)
    {
        var textChanged = StatusText.Text != title;
        StatusText.Text = title;
        SetDetail(detail);
        if (textChanged) { Motion.Swap(StatusText); if (DetailText.Visibility == Visibility.Visible) Motion.Swap(DetailText, 8); }

        var (glyph, c) = phase switch
        {
            Phase.Working => ("\uE895", Orange),
            Phase.Download => ("\uE896", Orange),
            Phase.Good => ("", Green),
            Phase.Warn => ("\uE7BA", Amber),
            _ => ("", Red)
        };

        var brush = new SolidColorBrush(c);
        IconBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
        BusyRing.Foreground = brush;
        BusyRing.IsActive = phase is Phase.Working or Phase.Download;

        // Good/Bad use drawn paths (perfectly centred); the rest use font glyphs.
        var good = phase == Phase.Good;
        var bad = phase == Phase.Bad;
        StatusIcon.Visibility = good || bad ? Visibility.Collapsed : Visibility.Visible;
        CheckPath.Visibility = good ? Visibility.Visible : Visibility.Collapsed;
        CrossPath.Visibility = bad ? Visibility.Visible : Visibility.Collapsed;
        if (!good && !bad) { StatusIcon.Glyph = glyph; StatusIcon.Foreground = brush; }

        if (_lastPhase != phase)
        {
            Motion.Pop(BadgeHost);
            if (good) Motion.Draw(CheckPath);
            if (bad) Motion.Draw(CrossPath);
        }
        _lastPhase = phase;
    }

    private void SetDetail(string? detail)
    {
        DetailText.Text = detail ?? "";
        DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetBar(bool active, bool indeterminate = true)
    {
        DownloadBar.IsIndeterminate = active && indeterminate;
        if (!active) DownloadBar.Value = 0;
        DownloadBar.Opacity = active ? 1 : 0; // keep layout stable
    }

    private void SetButtons(bool enabled)
    {
        PrimaryButton.IsEnabled = enabled;
        BuildPicker.IsEnabled = enabled;
        ArchPicker.IsEnabled = enabled;
        SelfUpdateButton.IsEnabled = enabled;
        GoStableButton.IsEnabled = enabled;
        ChangesButton.IsEnabled = enabled;
        InstallSelectedButton.IsEnabled = enabled && BuildPicker.SelectedItem is not null;
        LaunchButton.IsEnabled = enabled && VlcLocator.FindVlcExe() is not null;
        if (enabled) RefreshCacheText(); else ClearCacheButton.IsEnabled = false;
    }

    /// <summary>Turns download progress into the status line, bar and "12 MB/s - 1m 04s left" detail.</summary>
    private IProgress<DownloadProgress> MakeProgress(string title) => new Progress<DownloadProgress>(p =>
    {
        if (_closed) return;

        if (p.Note is not null)
        {
            StatusText.Text = "Reconnecting\u2026";
            SetDetail(p.Note);
            DownloadBar.IsIndeterminate = true;
            return;
        }

        DownloadBar.IsIndeterminate = p.Total <= 0;
        if (p.Total > 0) DownloadBar.Value = p.Percent;
        StatusText.Text = p.Total > 0 ? $"{title}\u2026 {p.Percent:0}%" : $"{title}\u2026";

        var parts = new List<string> { p.Total > 0 ? $"{Bytes(p.Received)} of {Bytes(p.Total)}" : Bytes(p.Received) };
        if (p.BytesPerSecond > 0) parts.Add($"{Bytes(p.BytesPerSecond)}/s");
        if (p.Eta is { } eta) parts.Add($"{FormatEta(eta)} left");
        SetDetail(string.Join("   \u00B7   ", parts));
    });

    // ---------- events ----------

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pending is { } build) await RunGuardedAsync(() => InstallBuildAsync(build));
        else await RunGuardedAsync(CheckFlowAsync);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        _opCts?.Cancel();
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e) => LaunchVlc();

    private void ConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc");
        if (!Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void Option_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        _state.AutoUpdate = AutoToggle.IsOn;
        _state.LaunchAfterUpdate = LaunchToggle.IsOn;
        _state.CloseAfterUpdate = CloseToggle.IsOn;
        _state.ReopenIfWasRunning = ReopenToggle.IsOn;
        _state.AutoUpdateSelf = SelfToggle.IsOn;
        StateStore.Save(_state);
    }

    private async void ArchPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || ArchPicker.SelectedItem is not ComboBoxItem { Tag: string key }) return;
        if (_state.ArchOverride == key) return;

        _state.ArchOverride = key;
        StateStore.Save(_state);
        await RunGuardedAsync(CheckFlowAsync); // different channel: look again
    }

    private void BuildPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        InstallSelectedButton.IsEnabled = !_busy && BuildPicker.SelectedItem is not null;

    private async void InstallSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (BuildPicker.SelectedItem is not BuildItem item) return;
        await RunGuardedAsync(async () =>
        {
            SetStatus("Preparing\u2026", item.Folder, Phase.Working);

            BuildInfo? build = null;
            try { build = await _updates.ResolveAsync(item.Folder, Ct); }
            catch (HttpRequestException) { /* offline: the cache may still have it */ }
            build ??= InstallerCache.TryGetBuild(item.Folder, _target);

            await InstallBuildAsync(build
                ?? throw new InvalidOperationException("That build has no .exe installer, and nothing is cached for it."));
        });
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        InstallerCache.Clear();
        RefreshCacheText();
        FillBuildPicker();
    }

    private async void SelfUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selfUpdate is { } info) await RunGuardedAsync(() => ApplySelfUpdateAsync(info));
    }

    // ---------- flows ----------

    private void LaunchVlc()
    {
        var exe = VlcLocator.FindVlcExe();
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    /// <summary>Common wrapper: busy state, buttons, cancellation and error handling for every action.</summary>
    private async Task RunGuardedAsync(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        SetButtons(false);
        SetBar(true);
        CancelButton.IsEnabled = true;

        _opCts?.Dispose();
        _opCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        try
        {
            await work();
        }
        catch (OperationCanceledException) when (_closed)
        {
            // window closed
        }
        catch (OperationCanceledException) when (_opCts.IsCancellationRequested)
        {
            SetStatus("Cancelled", "Nothing was installed. A partly downloaded file is kept and will resume next time.", Phase.Warn);
        }
        catch (OperationCanceledException)
        {
            SetStatus("The server took too long", "Check your connection and press Check again.", Phase.Bad);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            SetStatus("Update cancelled", "The UAC prompt was declined.", Phase.Warn);
        }
        catch (Exception ex)
        {
            SetStatus("Something went wrong", ex.Message, Phase.Bad);
        }
        finally
        {
            _busy = false;
            if (!_closed)
            {
                CancelButton.Visibility = Visibility.Collapsed;
                SetBar(false);
                SetButtons(true);
                RefreshActions();
            }
        }
    }

    private (VlcInstall? Install, VlcArch Target) ResolveTarget()
    {
        var inst = VlcLocator.Find();
        var target = ArchInfo.ParseKey(_state.ArchOverride) ?? inst?.Arch ?? ArchInfo.Native;
        if (!ArchInfo.Supported.Contains(target)) target = ArchInfo.Native;
        return (inst, target);
    }

    private async Task CheckFlowAsync()
    {
        _pending = null;

        if (Environment.OSVersion.Version.Build < 26200)
        {
            SetStatus("Windows 11 version 25H2 or newer required", "This updater doesn't support older Windows. Update Windows and try again.", Phase.Bad);
            return;
        }

        SetStatus("Checking for updates\u2026", "Contacting GitHub and artifacts.videolan.org", Phase.Working);

        // 1) This updater first. If it replaced itself, this process is exiting.
        if (await SelfUpdateCheckAsync()) return;

        // 2) Then VLC.
        var (vlc, target) = ResolveTarget();
        _target = target;
        _updates.Arch = target;

        SetStatus("Checking for VLC updates\u2026", $"{target.Label()} nightly channel", Phase.Working);
        var folders = await _updates.ListFoldersAsync(Ct);
        var latest = await _updates.GetLatestAsync(folders, Ct);
        _folders = folders;
        _latest = latest;

        _state.LastChecked = DateTime.Now;
        FillBuildPicker();
        SetTiles(_state.InstalledBuild, latest, vlc?.Arch ?? ArchInfo.ParseKey(_state.InstalledArch));

        StateStore.Save(_state);
        RefreshLastChecked();
        // Left the nightly channel on purpose: stay out of the way until the user comes back.
        if (_state.OnStable)
        {
            _pending = latest;
            SetStatus("Nightly updates are off",
                $"VLC is on the stable release{(_state.StableVersion is { } sv ? $" {sv}" : "")}. Press Back to nightly to return.", Phase.Warn);
            return;
        }

        // First run with VLC already installed: assume latest, just remember it.
        if (_state.InstalledBuild is null && vlc is not null && (vlc.Arch is null || vlc.Arch == target))
        {
            _state.InstalledBuild = latest.Folder;
            _state.InstalledHash = latest.Hash;
            _state.InstalledArch = target.Key();
            StateStore.Save(_state);
            RefreshLastChecked();
            SetTiles(latest.Folder, latest, target);
            SetStatus("You're all set", "First run: assuming your VLC is already on the latest nightly.", Phase.Good);
            return;
        }

        StateStore.Save(_state);
        RefreshLastChecked();

        var archSwitch = vlc?.Arch is { } have && have != target;

        if (vlc is not null && !archSwitch && string.CompareOrdinal(_state.InstalledBuild, latest.Folder) >= 0)
        {
            SetStatus("VLC is up to date", $"You're on the newest {target.Label()} nightly.", Phase.Good);
            return;
        }

        // Never switch CPU type behind the user's back, even with auto-update on.
        if (archSwitch)
        {
            _pending = latest;
            SetStatus("Switch VLC build?",
                $"{vlc!.Arch!.Value.Label()}  \u2192  {target.Label()}  ({latest.Folder})", Phase.Warn);
            return;
        }

        if (_state.IsPaused)
        {
            _pending = latest;
            SetStatus($"Updates paused until {_state.PausedUntil:MMM d}",
                $"{_state.InstalledBuild ?? "Not installed"}  \u2192  {latest.Folder}. Press Update now to install anyway.", Phase.Warn);
            return;
        }

        if (!_state.AutoUpdate)
        {
            _pending = latest;
            SetStatus("Update available",
                $"{_state.InstalledBuild ?? "Not installed"}  \u2192  {latest.Folder}", Phase.Warn);
            return;
        }

        await InstallBuildAsync(latest);
    }

    // ---------- updating this updater ----------

    /// <summary>Returns true if the app is restarting into a new version (so the caller must stop).</summary>
    private async Task<bool> SelfUpdateCheckAsync()
    {
        SelfUpdateBar.IsOpen = false;
        _selfUpdate = null;

        // A copy run from a build folder or debugger must never overwrite the installed app.
        if (!SelfUpdater.IsInstalledCopy()) return false;

        SelfUpdateInfo? info;
        try { info = await SelfUpdater.CheckAsync(Ct); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested || _opCts!.IsCancellationRequested) { throw; }
        catch { return false; } // GitHub unreachable or rate-limited: not worth blocking the VLC check
        if (info is null) return false;

        _selfUpdate = info;

        if (_state.AutoUpdateSelf)
        {
            try
            {
                await ApplySelfUpdateAsync(info);
                return true;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                ShowSelfUpdateBar(info, $"Automatic update failed: {ex.Message}", InfoBarSeverity.Warning);
                return false; // a broken self-update must not stop VLC from updating
            }
        }

        ShowSelfUpdateBar(info, $"Version {info.Version} is available (you have {Net.CurrentVersion}).", InfoBarSeverity.Informational);
        return false;
    }

    private void ShowSelfUpdateBar(SelfUpdateInfo info, string message, InfoBarSeverity severity)
    {
        SelfUpdateBar.Severity = severity;
        SelfUpdateBar.Message = message;
        SelfUpdateButton.Content = "Update now";
        SelfUpdateBar.IsOpen = true;
    }

    private async Task ApplySelfUpdateAsync(SelfUpdateInfo info)
    {
        SelfUpdateBar.IsOpen = false;
        SetStatus("Updating the updater\u2026", $"v{Net.CurrentVersion}  \u2192  v{info.Version}", Phase.Download);
        SetBar(true, indeterminate: false);

        var path = await SelfUpdater.DownloadAsync(info, MakeProgress("Updating the updater"), Ct);

        SetStatus("Restarting\u2026", $"Installing v{info.Version}. This window reopens by itself.", Phase.Working);
        SetBar(true);
        SelfUpdater.LaunchInstaller(path);

        await Task.Delay(400); // let the installer window appear, then get out of its way
        Close();
    }

    // ---------- installing VLC ----------

    private async Task InstallBuildAsync(BuildInfo build)
    {
        _pending = null;
        try
        {
            await InstallCoreAsync(build);
        }
        catch
        {
            _pending = build; // let "Update now" retry without another network check
            throw;
        }
    }

    private async Task InstallCoreAsync(BuildInfo build)
    {
        var current = VlcLocator.Find();
        if (current?.Arch is { } have && have != build.Arch && !await ConfirmArchSwitchAsync(have, build.Arch))
        {
            SetStatus("Cancelled", "VLC was left as it is.", Phase.Warn);
            _pending = build;
            return;
        }

        // Get the installer first while VLC keeps running: only close it right before installing.
        var installer = await EnsureInstallerAsync(build);

        var (closed, wasRunning) = await PrepareVlcAsync();
        if (!closed)
        {
            SetStatus("Update cancelled", "VLC is still running. Close it and press Update now.", Phase.Warn);
            _pending = build;
            return;
        }

        var installed = false;
        try
        {
            SetStatus("Installing\u2026", "Approve the UAC prompt to continue.", Phase.Working);
            SetBar(true);

            var code = await UpdateService.InstallAsync(installer);
            if (code != 0)
            {
                SetStatus("Retrying the install\u2026", $"The installer exited with code {code}. Trying once more.", Phase.Working);
                await Task.Delay(2000, Ct);
                code = await UpdateService.InstallAsync(installer);
            }
            if (code != 0) throw new InvalidOperationException($"The VLC installer exited with code {code}.");

            installed = true;
        }
        finally
        {
            // If we closed VLC and then the install failed or UAC was declined, put the user's VLC back.
            if (!installed && wasRunning && _state.ReopenIfWasRunning) LaunchVlc();
        }

        // Make sure it really landed, and as the architecture we meant to install.
        var after = VlcLocator.Find(build.Arch)
                    ?? throw new InvalidOperationException("The installer finished, but vlc.exe wasn't found afterwards.");
        var actualArch = after.Arch ?? build.Arch;

        if (_state.InstalledBuild != build.Folder)
        {
            _state.PreviousBuild = _state.InstalledBuild;
            _state.PreviousHash = _state.InstalledHash;
        }
        _state.InstalledBuild = build.Folder;
        _state.InstalledHash = build.Hash;
        _state.InstalledArch = actualArch.Key();
        _state.OnStable = false;
        _state.StableVersion = null;
        StateStore.Save(_state);

        InstallerCache.Prune(build.Arch, protect: installer); // keep the last few for offline rollback
        FillBuildPicker();
        RefreshCacheText();
        SetTiles(build.Folder, null, actualArch);

        var note = actualArch != build.Arch
            ? $" (Heads up: the installed vlc.exe is {actualArch.Label()}, not {build.Arch.Label()}.)"
            : "";
        SetStatus("Updated successfully",
            $"VLC is now on {build.Folder} ({actualArch.Label()}). Your settings were kept.{note}", Phase.Good);

        if (_state.LaunchAfterUpdate || (wasRunning && _state.ReopenIfWasRunning)) LaunchVlc();
        if (_state.CloseAfterUpdate)
        {
            await Task.Delay(1500);
            Close();
        }
    }

    private async Task<string> EnsureInstallerAsync(BuildInfo build)
    {
        var stage = new Progress<InstallStage>(s =>
        {
            if (_closed) return;
            switch (s)
            {
                case InstallStage.UsingCache:
                    CancelButton.Visibility = Visibility.Collapsed;
                    SetStatus("Checking the cached installer\u2026", build.ExeName, Phase.Working);
                    SetBar(true);
                    break;
                case InstallStage.Downloading:
                    SetStatus("Downloading\u2026", build.ExeName, Phase.Download);
                    SetBar(true, indeterminate: false);
                    CancelButton.IsEnabled = true;
                    CancelButton.Visibility = Visibility.Visible;
                    break;
                case InstallStage.Verifying:
                    CancelButton.Visibility = Visibility.Collapsed;
                    SetStatus("Verifying download\u2026", "Checking SHA512", Phase.Working);
                    SetBar(true);
                    break;
            }
        });

        try
        {
            return await _updates.EnsureInstallerAsync(build, MakeProgress("Downloading"), stage, Ct);
        }
        finally
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<bool> ConfirmArchSwitchAsync(VlcArch from, VlcArch to)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = $"Switch VLC to the {to.Label()} build?",
            Content = $"VLC on this PC is {from.Label()}. This installs the {to.Label()} nightly instead. " +
                      "Your VLC settings are kept, but VLC's own installer decides what happens to the old install.",
            PrimaryButtonText = $"Install {to.Label()}",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static bool VlcRunning()
    {
        var procs = Process.GetProcessesByName("vlc");
        var running = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        return running;
    }

    /// <summary>
    /// If VLC is open: ask, close it politely, wait for it to actually exit, and only offer a force-close if it
    /// refuses. Returns (VLC is closed, VLC had been running).
    /// </summary>
    private async Task<(bool Closed, bool WasRunning)> PrepareVlcAsync()
    {
        if (!VlcRunning()) return (true, false);

        var ask = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "VLC is running",
            Content = _state.ReopenIfWasRunning
                ? "VLC has to close before it can be updated. I'll close it gently and reopen it when the update is done."
                : "VLC has to close before it can be updated. Close it now?",
            PrimaryButtonText = "Close VLC and update",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return (false, true);

        SetStatus("Closing VLC\u2026", "Waiting for it to shut down cleanly.", Phase.Working);
        SetBar(true);

        foreach (var p in Process.GetProcessesByName("vlc"))
        {
            try { p.CloseMainWindow(); } catch { }
            p.Dispose();
        }

        // Up to 10 seconds for a clean exit (it may be saving its settings).
        for (var i = 0; i < 20 && VlcRunning(); i++) await Task.Delay(500, Ct);

        if (VlcRunning())
        {
            var force = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "VLC isn't closing",
                Content = "It may be busy or showing a prompt. Force it to close? Anything unsaved in VLC will be lost.",
                PrimaryButtonText = "Force close",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await force.ShowAsync() != ContentDialogResult.Primary) return (false, true);

            foreach (var p in Process.GetProcessesByName("vlc"))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                p.Dispose();
            }
            for (var i = 0; i < 10 && VlcRunning(); i++) await Task.Delay(300, Ct);
        }

        return (!VlcRunning(), true);
    }

    // ---------- what changed ----------

    /// <summary>The two builds worth comparing right now, or null if there's nothing to show.</summary>
    private ChangeRange? CurrentRange()
    {
        var installed = _state.InstalledBuild;

        // Update waiting: installed -> latest.
        if (installed is not null && _latest is { } lat && lat.Arch == _target &&
            string.CompareOrdinal(installed, lat.Folder) < 0)
            return new ChangeRange(installed, _state.InstalledHash, lat.Folder, lat.Hash, "What's new in the latest build");

        // Up to date: show what the last update brought.
        if (installed is not null && _state.PreviousBuild is { } prev &&
            string.CompareOrdinal(prev, installed) < 0)
            return new ChangeRange(prev, _state.PreviousHash, installed, _state.InstalledHash, "What changed in your last update");

        return null;
    }

    private void RefreshChangesButton()
    {
        var range = CurrentRange();
        ChangesButton.Visibility = range is null ? Visibility.Collapsed : Visibility.Visible;
        if (range is not null) ChangesLabel.Text = range.Label;
    }

    private async void ChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRange() is not { } range) return;

        ChangesButton.IsEnabled = false;
        ChangesLabel.Text = "Loading changes\u2026";
        try
        {
            var log = await ChangelogService.GetAsync(range, _cts.Token);
            await ShowChangesDialogAsync(range, log);
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex)
        {
            await ShowMessageAsync("Couldn't load the changes", ex.Message);
        }
        finally
        {
            if (!_closed)
            {
                ChangesButton.IsEnabled = !_busy;
                RefreshChangesButton();
            }
        }
    }

    private async Task ShowChangesDialogAsync(ChangeRange range, Changelog log)
    {
        var caption = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
        var key = AiSummaryService.Unprotect(_state.GeminiKeyProtected);
        // The ONLY gate for talking to Google: switch on + a key saved + something to summarize.
        var useAi = _state.AiSummaryEnabled && !string.IsNullOrWhiteSpace(key) && log.Commits.Count > 0;

        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(new TextBlock
        {
            Text = $"{range.FromFolder}  \u2192  {range.ToFolder}   \u00B7   {log.Total} change{(log.Total == 1 ? "" : "s")}",
            Style = caption, Opacity = 0.7
        });

        using var aiCts = new CancellationTokenSource();

        // ---- AI summary: always at the very top ----
        if (useAi)
        {
            var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            var bar = new ProgressBar { IsIndeterminate = true, Foreground = new SolidColorBrush(Orange) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(new FontIcon { Glyph = "\uE945", FontSize = 14, Foreground = new SolidColorBrush(Orange) });
            head.Children.Add(new TextBlock { Text = "AI summary", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            head.Children.Add(new TextBlock { Text = "experimental", Style = caption, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });

            var inner = new StackPanel { Spacing = 8 };
            inner.Children.Add(head);
            inner.Children.Add(summary);
            inner.Children.Add(bar);
            root.Children.Add(new Border
            {
                Style = (Style)Root.Resources["CardBorder"], // theme-aware, same look as the main window cards
                Padding = new Thickness(14), Child = inner
            });

            var cacheKey = $"{range.FromFolder}>{range.ToFolder}>{_state.GeminiModel}";
            if (SummaryCache.TryGetValue(cacheKey, out var cached))
            {
                summary.Text = cached;
                bar.Visibility = Visibility.Collapsed;
            }
            else
            {
                summary.Text = "Reading the changes\u2026";
                summary.Opacity = 0.6;
                var model = _state.GeminiModel;
                _ = Task.Run(async () =>
                {
                    string text; bool ok = true;
                    try { text = await AiSummaryService.SummarizeAsync(key!, model, log.Commits, log.Total, aiCts.Token); }
                    catch (OperationCanceledException) when (aiCts.IsCancellationRequested) { return; }
                    catch (Exception ex) { ok = false; text = "Couldn't get a summary: " + ex.Message; }

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (aiCts.IsCancellationRequested) return;
                        if (ok) SummaryCache[cacheKey] = text;
                        summary.Text = text;
                        summary.Opacity = ok ? 1 : 0.7;
                        bar.Visibility = Visibility.Collapsed;
                        Motion.Swap(summary, 8, 320);
                    });
                });
            }
        }

        // ---- raw commits: directly underneath ----
        var list = new StackPanel { Spacing = 10 };

        if (log.Approximate)
            list.Children.Add(new TextBlock
            {
                Text = "Matched by build time, so it may include a few extra commits or miss one at the edges.",
                Style = caption, Opacity = 0.6, TextWrapping = TextWrapping.Wrap
            });

        if (log.Commits.Count == 0)
            list.Children.Add(new TextBlock { Text = "No commits found for this range.", TextWrapping = TextWrapping.Wrap });

        foreach (var c in log.Commits)
        {
            var title = new TextBlock { Text = c.Title, TextWrapping = TextWrapping.Wrap };
            UIElement head = Uri.TryCreate(c.Url, UriKind.Absolute, out var uri)
                ? new HyperlinkButton { Padding = new Thickness(0), NavigateUri = uri, Content = title, HorizontalAlignment = HorizontalAlignment.Left }
                : title;

            var meta = string.Join("  \u00B7  ", new[]
            {
                c.ShortId,
                c.Author,
                c.Date?.ToString("MMM d, HH:mm", CultureInfo.InvariantCulture)
            }.Where(x => !string.IsNullOrEmpty(x)));

            var item = new StackPanel { Spacing = 0 };
            item.Children.Add(head);
            item.Children.Add(new TextBlock { Text = meta, Style = caption, Opacity = 0.6 });
            list.Children.Add(item);
        }

        if (log.Total > log.Commits.Count)
            list.Children.Add(new TextBlock
            {
                Text = $"Showing the newest {log.Commits.Count} of {log.Total}. Open GitLab for the full list.",
                Style = caption, Opacity = 0.6, TextWrapping = TextWrapping.Wrap
            });

        root.Children.Add(new Expander
        {
            Header = $"Raw commits ({log.Total})",
            IsExpanded = !useAi, // with a summary on top, keep the list tucked away until asked for
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new ScrollViewer { MaxHeight = 280, Content = list, Padding = new Thickness(0, 0, 12, 0) }
        });

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = range.Label,
            Content = new ScrollViewer { MaxHeight = 520, Content = root, Padding = new Thickness(0, 0, 12, 0) },
            PrimaryButtonText = "Open on GitLab",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        aiCts.Cancel();
        if (result == ContentDialogResult.Primary)
            Process.Start(new ProcessStartInfo(log.CompareUrl) { UseShellExecute = true });
    }

    // ---------- pause ----------

    private void RefreshPauseUi()
    {
        PauseBadge.Visibility = _state.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        PauseClearButton.IsEnabled = _state.IsPaused;
        PauseNote.Text = _state.IsPaused
            ? $"Paused through {_state.PausedUntil:dddd, MMM d}. No installs and no notifications until then. \"Update now\" still works."
            : "Not paused.";
    }

    private void PauseDate_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_loadingUi) return;
        _state.PausedUntil = args.NewDate?.Date;
        StateStore.Save(_state);
        RefreshPauseUi();
        if (_busy) return;
        // Re-evaluate the status line so it matches the new setting without another network round trip.
        if (_pending is { } p && !_state.OnStable)
            SetStatus(_state.IsPaused ? $"Updates paused until {_state.PausedUntil:MMM d}" : "Update available",
                _state.IsPaused ? $"{_state.InstalledBuild ?? "Not installed"}  \u2192  {p.Folder}. Press Update now to install anyway."
                                : $"{_state.InstalledBuild ?? "Not installed"}  \u2192  {p.Folder}", Phase.Warn);
    }

    private void PausePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string t } && int.TryParse(t, out var days))
            PauseDate.Date = new DateTimeOffset(DateTime.Today.AddDays(days));
    }

    private void PauseClear_Click(object sender, RoutedEventArgs e) => PauseDate.Date = null;

    // ---------- experimental AI summary ----------

    private void RefreshAiPanel()
    {
        AiPanel.Visibility = AiToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        AiNote.Text = AiToggle.IsOn
            ? "On: when you open \"What changed\", the commit titles are sent to Google's Gemini API to write a short summary. Needs your own API key (stored encrypted, only on this PC)."
            : "Off: this app makes no requests to Google at all. Turn on to get a 3-bullet plain-language summary above the raw commits.";
    }

    private void AiToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        _state.AiSummaryEnabled = AiToggle.IsOn;
        StateStore.Save(_state);
        RefreshAiPanel();
    }

    private void AiFields_LostFocus(object sender, RoutedEventArgs e) => SaveAiFields();

    private void SaveAiFields()
    {
        var key = AiKeyBox.Password.Trim();
        var model = AiModelBox.Text.Trim();
        _state.GeminiKeyProtected = key.Length == 0 ? null : AiSummaryService.Protect(key);
        _state.GeminiModel = model.Length == 0 ? AiSummaryService.DefaultModel : model;
        StateStore.Save(_state);
    }

    // ---------- back to stable ----------

    private async void GoStableButton_Click(object sender, RoutedEventArgs e)
    {
        var ask = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Go back to stable VLC?",
            Content = "This installs the latest stable VLC over your nightly. Your settings are kept.\n\n" +
                      "VLC Nightly Updater only works with nightly builds, so it will stop updating VLC and turn off background checks. " +
                      "You can come back any time with \"Back to nightly\".",
            PrimaryButtonText = "Install stable",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        await RunGuardedAsync(GoStableAsync);
    }

    private async Task GoStableAsync()
    {
        SetStatus("Looking up stable VLC\u2026", "get.videolan.org", Phase.Working);
        var (_, target) = ResolveTarget();
        var stable = await StableService.ResolveAsync(target, Ct);

        SetStatus("Downloading\u2026", stable.ExeName, Phase.Download);
        SetBar(true, indeterminate: false);
        CancelButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Visible;
        string installer;
        try { installer = await StableService.DownloadAsync(stable, MakeProgress("Downloading"), Ct); }
        finally { CancelButton.Visibility = Visibility.Collapsed; }

        var (closed, wasRunning) = await PrepareVlcAsync();
        if (!closed)
        {
            SetStatus("Cancelled", "VLC was left as it is.", Phase.Warn);
            return;
        }

        var installed = false;
        try
        {
            SetStatus("Installing\u2026", "Approve the UAC prompt to continue.", Phase.Working);
            SetBar(true);
            var code = await UpdateService.InstallAsync(installer);
            if (code != 0) throw new InvalidOperationException($"The VLC installer exited with code {code}.");
            installed = true;
        }
        finally
        {
            if (!installed && wasRunning && _state.ReopenIfWasRunning) LaunchVlc();
        }

        _state.OnStable = true;
        _state.StableVersion = stable.Version;
        _state.InstalledBuild = null;
        _state.InstalledHash = null;
        _state.PreviousBuild = null;
        _state.PreviousHash = null;
        _state.BackgroundCheck = false;
        StateStore.Save(_state);

        _pending = null;
        await Task.Run(BackgroundCheck.Disable);
        _loadingUi = true;
        BackgroundToggle.IsOn = false;
        _loadingUi = false;
        BackgroundNote.Text = BackgroundNoteText(false);
        TestNotifyButton.Visibility = Visibility.Collapsed;

        SetTiles(null, null, null);
        SetStatus("Back on stable VLC", $"VLC {stable.Version} is installed. Nightly updates are off. To return, press Check again, then Back to nightly.", Phase.Good);
        if (wasRunning && _state.ReopenIfWasRunning) LaunchVlc();
    }

    private async Task ShowMessageAsync(string title, string message) =>
        await new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK"
        }.ShowAsync();
}
