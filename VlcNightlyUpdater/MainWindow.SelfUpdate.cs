using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>Updating THIS app from its GitHub Releases (stable, or pre-releases when the setting is on).</summary>
public sealed partial class MainWindow
{
    /// <summary>Returns true if the app is restarting into a new version (so the caller must stop).</summary>
    private async Task<bool> SelfUpdateCheckAsync()
    {
        SelfUpdateBar.IsOpen = false;
        _selfUpdate = null;

        // A copy run from a build folder or debugger must never overwrite the installed app.
        if (!SelfUpdater.IsInstalledCopy()) return false;

        SelfUpdateInfo? info;
        try { info = await SelfUpdater.CheckAsync(_state.AllowPreRelease, Ct); }
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
                ShowSelfUpdateBar(Strings.SelfFailed(ex.Message), InfoBarSeverity.Warning);
                return false; // a broken self-update must not stop VLC from updating
            }
        }

        ShowSelfUpdateBar(Strings.SelfAvailable(info), InfoBarSeverity.Informational);
        return false;
    }

    private void ShowSelfUpdateBar(string message, InfoBarSeverity severity)
    {
        SelfUpdateBar.Severity = severity;
        SelfUpdateBar.Message = message;
        SelfUpdateBar.IsOpen = true;
    }

    private async void SelfUpdateButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_selfUpdate is { } info) await RunGuardedAsync(() => ApplySelfUpdateAsync(info));
    }

    private async Task ApplySelfUpdateAsync(SelfUpdateInfo info)
    {
        SelfUpdateBar.IsOpen = false;
        Status.Set(Strings.SelfUpdating, Strings.SelfUpdateRange(info.Version), Phase.Download);
        Status.SetBar(true, indeterminate: false);

        var path = await SelfUpdater.DownloadAsync(info, MakeProgress(Strings.SelfUpdating), Ct);

        Status.Set(Strings.Restarting, Strings.RestartingDetail(info.Version), Phase.Working);
        Status.SetBar(true);
        SelfUpdater.LaunchInstaller(path);

        await Task.Delay(400); // let the installer window appear, then get out of its way
        Close();
    }
}
