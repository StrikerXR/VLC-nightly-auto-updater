using System.Net;
using System.Diagnostics;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Windows 7 doesn't enable TLS 1.2 for apps by default; VideoLAN's server needs it.
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2

        using var single = new Mutex(true, @"Local\VlcNightlyUpdater.Legacy.SingleInstance", out var first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new LegacyForm());
    }
}

/// <summary>Plain, dependency-free window for Windows 7 / 8 / 8.1 (and anything else that can't run WinUI 3).</summary>
internal sealed class LegacyForm : Form
{
    private readonly UpdateService _updates = new();
    private readonly AppState _state = StateStore.Load();
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _op;
    private BuildInfo? _pending;
    private bool _busy, _loading = true;

    private readonly Label _status = new() { AutoSize = false, Height = 26, Dock = DockStyle.Top, Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
    private readonly Label _detail = new() { AutoSize = false, Height = 40, Dock = DockStyle.Top, ForeColor = SystemColors.GrayText };
    private readonly Label _tiles = new() { AutoSize = false, Height = 44, Dock = DockStyle.Top };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 14, Style = ProgressBarStyle.Continuous };
    private readonly Button _primary = new() { Text = "Check again", Width = 130, Height = 30 };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 80, Height = 30, Visible = false };
    private readonly CheckBox _auto = new() { Text = "Update automatically when opened", AutoSize = true };
    private readonly CheckBox _launch = new() { Text = "Open VLC after updating", AutoSize = true };
    private readonly CheckBox _cache = new() { Text = "Keep installers for offline rollback", AutoSize = true };
    private readonly CheckBox _reopen = new() { Text = "Reopen VLC if it had to be closed", AutoSize = true };
    private readonly Label _pause = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _resume = new() { Text = "Resume updates", AutoSize = true, Visible = false };

    public LegacyForm()
    {
        Text = "VLC Nightly Updater";
        ClientSize = new Size(480, 400);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var opts = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 150, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        opts.Controls.AddRange(new Control[] { _auto, _launch, _reopen, _cache, _pause, _resume });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange(new Control[] { _primary, _cancel });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        // Docking order: last added is laid out first, so add bottom-up.
        body.Controls.Add(buttons);
        body.Controls.Add(opts);
        body.Controls.Add(_bar);
        body.Controls.Add(_tiles);
        body.Controls.Add(_detail);
        body.Controls.Add(_status);
        Controls.Add(body);

        _auto.Checked = _state.AutoUpdate;
        _launch.Checked = _state.LaunchAfterUpdate;
        _reopen.Checked = _state.ReopenIfWasRunning;
        _cache.Checked = _state.CacheInstallers;
        _loading = false;

        foreach (var c in new[] { _auto, _launch, _reopen, _cache })
            c.CheckedChanged += (_, _) => SaveOptions();
        _primary.Click += async (_, _) => { if (_pending is { } b) await RunAsync(() => InstallAsync(b)); else await RunAsync(CheckAsync); };
        _cancel.Click += (_, _) => _op?.Cancel();
        _resume.Click += async (_, _) => { _state.PausedUntil = null; StateStore.Save(_state); RefreshPause(); await RunAsync(CheckAsync); };
        FormClosed += (_, _) => _cts.Cancel();
        Shown += async (_, _) => { RefreshPause(); await RunAsync(CheckAsync); };
        SetTiles(null);
    }

    private void SaveOptions()
    {
        if (_loading) return;
        _state.AutoUpdate = _auto.Checked;
        _state.LaunchAfterUpdate = _launch.Checked;
        _state.ReopenIfWasRunning = _reopen.Checked;
        _state.CacheInstallers = _cache.Checked;
        StateStore.Save(_state);
        if (!_cache.Checked) InstallerCache.Clear();
    }

    private bool Paused => _state.IsPaused && !_state.OnStable;

    private void RefreshPause()
    {
        _pause.Text = Paused ? $"Paused until {_state.PausedUntil:MMM d}. No checks or installs until then." : "";
        _resume.Visible = Paused;
        _primary.Enabled = !_busy && !Paused;
        if (Paused) _primary.Text = "Paused";
    }

    private void Set(string status, string? detail = null) { _status.Text = status; _detail.Text = detail ?? ""; }

    private void SetTiles(string? latest) =>
        _tiles.Text = $"Installed: {_state.InstalledBuild ?? "\u2014"}\r\nLatest:      {latest ?? "\u2014"}";

    private async Task RunAsync(Func<Task> work)
    {
        if (_busy) return;
        _busy = true; _primary.Enabled = false;
        _bar.Style = ProgressBarStyle.Marquee;
        _op = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        try { await work(); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { return; }
        catch (OperationCanceledException) { Set("Cancelled", "Nothing was installed. A partial download resumes next time."); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { Set("Update cancelled", "The UAC prompt was declined."); }
        catch (Exception ex) { Set("Something went wrong", ex.Message); }
        finally
        {
            _busy = false;
            if (!IsDisposed)
            {
                _bar.Style = ProgressBarStyle.Continuous; _bar.Value = 0; _cancel.Visible = false;
                _primary.Text = Paused ? "Paused" : _pending is not null ? "Update now" : "Check again";
                _primary.Enabled = !Paused;
            }
        }
    }

    private async Task CheckAsync()
    {
        _pending = null;
        if (Paused) { Set($"Updates paused until {_state.PausedUntil:MMM d}", "Resume updates to check again."); return; }
        if (_state.OnStable) { Set("Nightly updates are off", "VLC is on the stable release."); return; }

        Set("Checking for updates\u2026", "artifacts.videolan.org");
        var vlc = VlcLocator.Find();
        _updates.Arch = VlcArch.X64;
        var folders = await _updates.ListFoldersAsync(_op!.Token);
        var latest = await _updates.GetLatestAsync(folders, _op.Token);
        SetTiles(latest.Folder);

        if (_state.InstalledBuild is null && vlc is not null)
        {
            _state.InstalledBuild = latest.Folder; _state.InstalledHash = latest.Hash; _state.InstalledArch = "x64";
            StateStore.Save(_state); SetTiles(latest.Folder);
            Set("You're all set", "First run: assuming your VLC is already on the latest nightly.");
            return;
        }
        if (vlc is not null && string.CompareOrdinal(_state.InstalledBuild, latest.Folder) >= 0)
        {
            Set("VLC is up to date", "You're on the newest 64-bit nightly.");
            return;
        }

        _pending = latest;
        if (_state.AutoUpdate) await InstallAsync(latest);
        else Set("Update available", $"{_state.InstalledBuild ?? "Not installed"}  ->  {latest.Folder}");
    }

    private async Task InstallAsync(BuildInfo build)
    {
        _pending = build;
        var ct = _op!.Token;

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.Note is not null) { Set("Reconnecting\u2026", p.Note); return; }
            _bar.Style = p.Total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
            if (p.Total > 0) _bar.Value = Math.Min(100, (int)p.Percent);
            Set(p.Total > 0 ? $"Downloading\u2026 {p.Percent:0}%" : "Downloading\u2026", $"{p.Received / 1048576.0:0.0} MB" + (p.Total > 0 ? $" of {p.Total / 1048576.0:0.0} MB" : ""));
        });
        var stage = new Progress<InstallStage>(s =>
        {
            _cancel.Visible = s == InstallStage.Downloading;
            if (s == InstallStage.Verifying) { _bar.Style = ProgressBarStyle.Marquee; Set("Verifying download\u2026", "Checking SHA512"); }
            if (s == InstallStage.UsingCache) Set("Checking the cached installer\u2026", build.ExeName);
        });

        var installer = await _updates.EnsureInstallerAsync(build, progress, stage, ct);
        _cancel.Visible = false; _bar.Style = ProgressBarStyle.Marquee;

        var wasRunning = Process.GetProcessesByName("vlc").Length > 0;
        if (wasRunning)
        {
            if (MessageBox.Show(this, "VLC has to close before it can be updated. Close it now?", "VLC is running",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            { Set("Update cancelled", "VLC is still running. Close it and press Update now."); return; }

            foreach (var p in Process.GetProcessesByName("vlc")) { try { p.CloseMainWindow(); } catch { } }
            for (var i = 0; i < 20 && Process.GetProcessesByName("vlc").Length > 0; i++) await Task.Delay(500, ct);
            foreach (var p in Process.GetProcessesByName("vlc")) { try { p.Kill(); } catch { } }
        }

        Set("Installing\u2026", "Approve the UAC prompt to continue.");
        var code = await UpdateService.InstallAsync(installer);
        if (code != 0) { code = await UpdateService.InstallAsync(installer); }
        if (code != 0)
        {
            if (wasRunning && _state.ReopenIfWasRunning) LaunchVlc();
            throw new InvalidOperationException($"The VLC installer exited with code {code}.");
        }

        if (_state.InstalledBuild != build.Folder) { _state.PreviousBuild = _state.InstalledBuild; _state.PreviousHash = _state.InstalledHash; }
        _state.InstalledBuild = build.Folder; _state.InstalledHash = build.Hash; _state.InstalledArch = "x64";
        StateStore.Save(_state);

        if (_state.CacheInstallers) InstallerCache.Prune(build.Arch, protect: installer); else InstallerCache.Delete(installer);
        _pending = null;
        SetTiles(build.Folder);
        Set("Updated successfully", $"VLC is now on {build.Folder}. Your settings were kept.");
        if (_state.LaunchAfterUpdate || (wasRunning && _state.ReopenIfWasRunning)) LaunchVlc();
    }

    private static void LaunchVlc()
    {
        var exe = VlcLocator.FindVlcExe();
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }
}
