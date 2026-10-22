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

    private static readonly Color Orange = Color.FromArgb(255, 255, 136, 0);
    private static readonly Color Green = Color.FromArgb(255, 108, 203, 95);
    private static readonly Color Amber = Color.FromArgb(255, 252, 225, 0);
    private static readonly Color Red = Color.FromArgb(255, 255, 107, 107);

    private readonly UpdateService _updates = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly AppState _state = StateStore.Load();

    private BuildInfo? _pending;   // an update that's waiting for the user to press "Update now"
    private bool _busy, _started, _closed, _loadingUi;

    public MainWindow()
    {
        InitializeComponent();
        SetupWindow();

        var v = typeof(App).Assembly.GetName().Version;
        VersionText.Text = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";

        _loadingUi = true;
        AutoToggle.IsOn = _state.AutoUpdate;
        LaunchToggle.IsOn = _state.LaunchAfterUpdate;
        CloseToggle.IsOn = _state.CloseAfterUpdate;
        _loadingUi = false;

        SetTiles(_state.InstalledBuild, null);
        RefreshLastChecked();

        Closed += (_, _) => { _closed = true; _cts.Cancel(); };
        Root.Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            await RunGuardedAsync(CheckFlowAsync);
        };
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

        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); } catch { }

        // Size in DIPs -> pixels, then center on the current display.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        int w = (int)(540 * scale), h = (int)(560 * scale);
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

    private void SetTiles(string? installed, string? latest)
    {
        InstalledText.Text = installed ?? "\u2014";
        InstalledDate.Text = Pretty(installed);
        if (latest is not null)
        {
            LatestText.Text = latest;
            LatestDate.Text = Pretty(latest);
        }
    }

    private void RefreshLastChecked() =>
        LastCheckedText.Text = _state.LastChecked is { } t ? $"Last checked {t:t}" : "";

    private void RefreshPrimary()
    {
        var update = _pending is not null;
        PrimaryText.Text = update ? "Update now" : "Check again";
        PrimaryIcon.Glyph = update ? "\uE896" : "\uE72C";
    }

    private void FillBuildPicker(IEnumerable<string> folders) =>
        BuildPicker.ItemsSource = folders.Take(15)
            .Select(f => new BuildItem(f, Pretty(f) is { Length: > 0 } p ? $"{f}   \u00B7   {p}" : f))
            .ToList();

    private void SetStatus(string title, string? detail, Phase phase)
    {
        StatusText.Text = title;
        DetailText.Text = detail ?? "";
        DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;

        var (glyph, c) = phase switch
        {
            Phase.Working => ("\uE895", Orange),
            Phase.Download => ("\uE896", Orange),
            Phase.Good => ("\uE930", Green),
            Phase.Warn => ("\uE7BA", Amber),
            _ => ("\uE783", Red)
        };
        StatusIcon.Glyph = glyph;
        StatusIcon.Foreground = new SolidColorBrush(c);
        IconBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
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
        InstallSelectedButton.IsEnabled = enabled && BuildPicker.SelectedItem is not null;
        LaunchButton.IsEnabled = enabled && VlcLocator.FindVlcExe() is not null;
    }

    // ---------- events ----------

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pending is { } build) await RunGuardedAsync(() => InstallBuildAsync(build));
        else await RunGuardedAsync(CheckFlowAsync);
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
        StateStore.Save(_state);
    }

    private void BuildPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        InstallSelectedButton.IsEnabled = !_busy && BuildPicker.SelectedItem is not null;

    private async void InstallSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (BuildPicker.SelectedItem is not BuildItem item) return;
        await RunGuardedAsync(async () =>
        {
            SetStatus("Preparing…", item.Folder, Phase.Working);
            var build = await _updates.ResolveAsync(item.Folder, _cts.Token)
                        ?? throw new InvalidOperationException("That build has no .exe installer.");
            await InstallBuildAsync(build);
        });
    }

    // ---------- flows ----------

    private void LaunchVlc()
    {
        var exe = VlcLocator.FindVlcExe();
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    /// <summary>Common wrapper: busy state, buttons, and error handling for every action.</summary>
    private async Task RunGuardedAsync(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        SetButtons(false);
        SetBar(true);

        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // window closed
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
                SetBar(false);
                SetButtons(true);
                RefreshPrimary();
            }
        }
    }

    private async Task CheckFlowAsync()
    {
        _pending = null;

        if (Environment.OSVersion.Version.Build < 22000)
        {
            SetStatus("Windows 11 required", "This updater only supports Windows 11.", Phase.Bad);
            return;
        }

        SetStatus("Checking for updates…", "Contacting artifacts.videolan.org", Phase.Working);
        var folders = await _updates.ListFoldersAsync(_cts.Token);
        var latest = await _updates.GetLatestAsync(folders, _cts.Token);
        var vlcExe = VlcLocator.FindVlcExe();

        _state.LastChecked = DateTime.Now;
        FillBuildPicker(folders);
        SetTiles(_state.InstalledBuild, latest.Folder);

        // First run with VLC already installed: assume latest, just remember it.
        if (_state.InstalledBuild is null && vlcExe is not null)
        {
            _state.InstalledBuild = latest.Folder;
            StateStore.Save(_state);
            RefreshLastChecked();
            SetTiles(latest.Folder, latest.Folder);
            SetStatus("You're all set", "First run: assuming your VLC is already on the latest nightly.", Phase.Good);
            return;
        }

        StateStore.Save(_state);
        RefreshLastChecked();

        if (vlcExe is not null && string.CompareOrdinal(_state.InstalledBuild, latest.Folder) >= 0)
        {
            SetStatus("VLC is up to date", "You're on the newest nightly build.", Phase.Good);
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

    private async Task InstallBuildAsync(BuildInfo build)
    {
        _pending = null;

        if (!await CloseVlcIfRunningAsync())
        {
            SetStatus("Update cancelled", "VLC is still running. Close it and try again.", Phase.Warn);
            return;
        }

        SetStatus("Downloading…", build.ExeName, Phase.Download);
        SetBar(true, indeterminate: false);
        var progress = new Progress<double>(v =>
        {
            DownloadBar.Value = v;
            StatusText.Text = $"Downloading… {v:0}%";
        });
        var installer = await _updates.DownloadAsync(build, progress, _cts.Token);

        SetStatus("Verifying download…", "Checking SHA512", Phase.Working);
        SetBar(true);
        await _updates.VerifyAsync(installer, build, _cts.Token);

        SetStatus("Installing…", "Approve the UAC prompt to continue.", Phase.Working);
        var code = await UpdateService.InstallAsync(installer);
        if (code != 0)
            throw new InvalidOperationException($"Installer exited with code {code}.");

        _state.InstalledBuild = build.Folder;
        StateStore.Save(_state);
        SetTiles(build.Folder, null);
        try { File.Delete(installer); } catch { /* ignore */ }

        SetStatus("Updated successfully", $"VLC is now on {build.Folder}. Your settings were kept.", Phase.Good);

        if (_state.LaunchAfterUpdate) LaunchVlc();
        if (_state.CloseAfterUpdate)
        {
            await Task.Delay(1500);
            Close();
        }
    }

    private async Task<bool> CloseVlcIfRunningAsync()
    {
        var procs = Process.GetProcessesByName("vlc");
        if (procs.Length == 0) return true;

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "VLC is running",
            Content = "VLC needs to be closed before updating. Close it now?",
            PrimaryButtonText = "Close VLC and update",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;

        foreach (var p in procs) { try { p.CloseMainWindow(); } catch { } }
        await Task.Delay(3000);
        foreach (var p in Process.GetProcessesByName("vlc")) { try { p.Kill(); } catch { } }
        await Task.Delay(500);
        return Process.GetProcessesByName("vlc").Length == 0;
    }
}
