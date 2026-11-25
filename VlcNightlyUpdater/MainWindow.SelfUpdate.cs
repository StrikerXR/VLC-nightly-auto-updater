using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>
/// Updating THIS app from its GitHub Releases (stable, or pre-releases when the setting is on).
/// A new version first shows the full "What's new" dialog (once per run); "Update and restart" installs it.
/// A portable copy can't replace itself: the dialog's main button opens the download page instead.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Returns true if the app is restarting into a new version (so the caller must stop).</summary>
    private async Task<bool> SelfUpdateCheckAsync()
    {
        SelfUpdateBar.IsOpen = false;
        _selfUpdate = null;

        // A copy run from a build folder or debugger must never overwrite the installed app.
        // A portable copy may look for updates (and show their notes) but never installs one.
        var installed = SelfUpdater.IsInstalledCopy();
        if (!installed && !Portable.IsPortable) return false;

        SelfUpdateInfo? info;
        try { info = await SelfUpdater.CheckAsync(_state.AllowPreRelease, Ct); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested || _opCts!.IsCancellationRequested) { throw; }
        catch { return false; } // GitHub unreachable or rate-limited: not worth blocking the VLC check
        if (info is null) return false;

        _selfUpdate = info;
        SelfUpdateButton.Content = installed ? Strings.UpdateNow : Strings.OpenDownloadPage;

        // The full "What's new" dialog, once per run for each new version.
        if (_state.ShowWhatsNew && _promptedThisRun != info.Version.ToString())
        {
            _promptedThisRun = info.Version.ToString();
            if (await ShowUpdateDialogAsync(info, installed))
                return await InstallOrOpenAsync(info, installed);

            ShowSelfUpdateBar(installed ? Strings.SelfAvailable(info) : Strings.SelfAvailablePortable(info), InfoBarSeverity.Informational);
            return false;
        }

        // Dialog switched off: the old behaviour (silent when automatic updating is on, a banner otherwise).
        if (installed && _state.AutoUpdateSelf && !_state.ShowWhatsNew)
            return await InstallOrOpenAsync(info, installed: true);

        ShowSelfUpdateBar(installed ? Strings.SelfAvailable(info) : Strings.SelfAvailablePortable(info), InfoBarSeverity.Informational);
        return false;
    }

    /// <summary>Installs the update (installed copy) or opens the release page (portable). True = we are restarting.</summary>
    private async Task<bool> InstallOrOpenAsync(SelfUpdateInfo info, bool installed)
    {
        if (!installed)
        {
            OpenUrl(info.PageUrl);
            ShowSelfUpdateBar(Strings.SelfAvailablePortable(info), InfoBarSeverity.Informational);
            return false;
        }

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

    private void ShowSelfUpdateBar(string message, InfoBarSeverity severity)
    {
        SelfUpdateBar.Severity = severity;
        SelfUpdateBar.Message = message;
        SelfUpdateBar.IsOpen = true;
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no default browser: nothing sensible to do */ }
    }

    private async void SelfUpdateButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_selfUpdate is not { } info) return;
        if (!SelfUpdater.IsInstalledCopy()) { OpenUrl(info.PageUrl); return; }
        await RunGuardedAsync(() => ApplySelfUpdateAsync(info));
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
