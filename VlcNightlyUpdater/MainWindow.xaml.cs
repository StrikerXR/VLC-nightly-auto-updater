using System.Diagnostics;
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private static readonly Color Orange = Color.FromArgb(255, 255, 136, 0);
    private static readonly Color Green = Color.FromArgb(255, 108, 203, 95);
    private static readonly Color Amber = Color.FromArgb(255, 252, 225, 0);
    private static readonly Color Red = Color.FromArgb(255, 255, 107, 107);

    private readonly UpdateService _updates = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _busy;
    private bool _started;

    public MainWindow()
    {
        InitializeComponent();
        SetupWindow();

        Closed += (_, _) => _cts.Cancel();
        Root.Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            await RunAsync();
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
        int w = (int)(540 * scale), h = (int)(440 * scale);
        AppWindow.Resize(new SizeInt32(w, h));

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new PointInt32(
            area.X + (area.Width - w) / 2,
            area.Y + (area.Height - h) / 2));
    }

    // ---------- UI helpers ----------

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

    // ---------- events ----------

    private async void CheckButton_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var exe = VlcLocator.FindVlcExe();
        if (exe is null) return;
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    // ---------- main flow ----------

    private async Task RunAsync()
    {
        if (_busy) return;
        _busy = true;
        CheckButton.IsEnabled = false;
        LaunchButton.IsEnabled = false;
        SetBar(true);

        try
        {
            if (Environment.OSVersion.Version.Build < 22000)
            {
                SetStatus("Windows 11 required", "This updater only supports Windows 11.", Phase.Bad);
                return;
            }

            SetStatus("Checking for updates…", "Contacting artifacts.videolan.org", Phase.Working);
            var latest = await _updates.GetLatestAsync(_cts.Token);
            var state = StateStore.Load();
            var vlcExe = VlcLocator.FindVlcExe();

            LatestText.Text = latest.Folder;
            InstalledText.Text = state.InstalledBuild ?? "—";

            // First run with VLC already installed: assume latest, just remember it.
            if (state.InstalledBuild is null && vlcExe is not null)
            {
                state.InstalledBuild = latest.Folder;
                StateStore.Save(state);
                InstalledText.Text = latest.Folder;
                SetStatus("You're all set", "First run: assuming your VLC is already on the latest nightly.", Phase.Good);
                return;
            }

            if (vlcExe is not null && string.CompareOrdinal(state.InstalledBuild, latest.Folder) >= 0)
            {
                SetStatus("VLC is up to date", "You're on the newest nightly build.", Phase.Good);
                return;
            }

            // ---- Update needed ----
            SetStatus("Update available", $"{state.InstalledBuild ?? "Not installed"}  →  {latest.Folder}", Phase.Warn);

            if (!await CloseVlcIfRunningAsync())
            {
                SetStatus("Update cancelled", "VLC is still running. Close it and try again.", Phase.Warn);
                return;
            }

            SetStatus("Downloading…", latest.ExeName, Phase.Download);
            SetBar(true, indeterminate: false);
            var progress = new Progress<double>(v =>
            {
                DownloadBar.Value = v;
                StatusText.Text = $"Downloading… {v:0}%";
            });
            var installer = await _updates.DownloadAsync(latest, progress, _cts.Token);

            SetStatus("Verifying download…", "Checking SHA512", Phase.Working);
            SetBar(true);
            await _updates.VerifyAsync(installer, latest, _cts.Token);

            SetStatus("Installing…", "Approve the UAC prompt to continue.", Phase.Working);
            var code = await UpdateService.InstallAsync(installer);
            if (code != 0)
                throw new InvalidOperationException($"Installer exited with code {code}.");

            state.InstalledBuild = latest.Folder;
            StateStore.Save(state);
            InstalledText.Text = latest.Folder;
            try { File.Delete(installer); } catch { /* ignore */ }

            SetStatus("Updated successfully", $"VLC is now on {latest.Folder}. Your settings were kept.", Phase.Good);
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
            SetBar(false);
            CheckButton.IsEnabled = true;
            LaunchButton.IsEnabled = VlcLocator.FindVlcExe() is not null;
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
