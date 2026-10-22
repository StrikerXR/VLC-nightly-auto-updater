using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater;

public sealed partial class MainWindow : Window
{
    private readonly UpdateService _updates = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _busy;
    private bool _started;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(560, 340));
        Closed += (_, _) => _cts.Cancel();
        Root.Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            await RunAsync();
        };
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var exe = VlcLocator.FindVlcExe();
        if (exe is null) return;
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    private async Task RunAsync()
    {
        if (_busy) return;
        _busy = true;
        CheckButton.IsEnabled = false;
        DownloadBar.Value = 0;
        DetailText.Text = "";

        try
        {
            if (Environment.OSVersion.Version.Build < 22000)
            {
                Show("This updater is for Windows 11 only.", done: true);
                return;
            }

            Show("Checking for the latest nightly…");
            var latest = await _updates.GetLatestAsync(_cts.Token);
            var state = StateStore.Load();
            var vlcExe = VlcLocator.FindVlcExe();

            // First run with VLC already installed: assume it's already the latest, just remember it.
            if (state.InstalledBuild is null && vlcExe is not null)
            {
                state.InstalledBuild = latest.Folder;
                StateStore.Save(state);
                Show("First run: assuming your VLC is already on the latest nightly.", done: true);
                DetailText.Text = $"Latest build: {latest.Folder}  ({latest.ExeName})";
                return;
            }

            if (vlcExe is not null &&
                string.CompareOrdinal(state.InstalledBuild, latest.Folder) >= 0)
            {
                Show("VLC is up to date.", done: true);
                DetailText.Text = $"Installed build: {state.InstalledBuild}";
                return;
            }

            // ---- Update needed ----
            DetailText.Text = $"Installed: {state.InstalledBuild ?? "none"}  →  Latest: {latest.Folder}";

            if (!await CloseVlcIfRunningAsync())
            {
                Show("Update cancelled: VLC is still running.", done: true);
                return;
            }

            Show("Downloading…");
            DownloadBar.IsIndeterminate = false;
            var progress = new Progress<double>(v =>
            {
                DownloadBar.Value = v;
                StatusText.Text = $"Downloading… {v:0}%";
            });
            var installer = await _updates.DownloadAsync(latest, progress, _cts.Token);

            Show("Verifying SHA512…");
            await _updates.VerifyAsync(installer, latest, _cts.Token);

            Show("Installing silently (accept the UAC prompt)…");
            var code = await UpdateService.InstallAsync(installer);
            if (code != 0)
                throw new InvalidOperationException($"Installer exited with code {code}.");

            state.InstalledBuild = latest.Folder;
            StateStore.Save(state);
            try { File.Delete(installer); } catch { /* ignore */ }

            Show($"Updated to {latest.Folder}.", done: true);
            LaunchButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            // window closed
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Show("Update cancelled (UAC prompt was declined).", done: true);
        }
        catch (Exception ex)
        {
            Show("Something went wrong.", done: true);
            DetailText.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            CheckButton.IsEnabled = true;
            LaunchButton.IsEnabled = VlcLocator.FindVlcExe() is not null;
        }
    }

    private void Show(string status, bool done = false)
    {
        StatusText.Text = status;
        DownloadBar.IsIndeterminate = !done;
        if (done) DownloadBar.Value = DownloadBar.Value; // keep as is
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
