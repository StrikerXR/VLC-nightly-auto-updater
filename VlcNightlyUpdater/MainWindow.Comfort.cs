using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>Small comforts: keep the window on top, the check interval, "Reset settings" and "Stop skipping". Wiring is in MainWindow.xaml.cs.</summary>
public sealed partial class MainWindow
{
    private void ApplyAlwaysOnTop()
    {
        if (AppWindow.Presenter is OverlappedPresenter p) p.IsAlwaysOnTop = _state.AlwaysOnTop;
    }

    private void ApplyCheckInterval()
    {
        _autoCheckTimer.Stop();
        _autoCheckTimer.Interval = TimeSpan.FromHours(_state.AutoCheckHours);
        _autoCheckTimer.Start();
    }

    /// <summary>Puts every option back to its default after asking. Never touches VLC, the history, the cache or the background task.</summary>
    private async Task ResetSettingsAsync()
    {
        if (_busy) return;
        var ask = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.ResetTitle,
            Content = Strings.ResetBody,
            PrimaryButtonText = Strings.ResetConfirm,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Close
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;

        var d = new AppState();
        _state.AutoUpdate = d.AutoUpdate;
        _state.LaunchAfterUpdate = d.LaunchAfterUpdate;
        _state.WaitForVlc = d.WaitForVlc;
        _state.CacheKeep = d.CacheKeep;
        _state.AutoCheckHours = d.AutoCheckHours;
        _state.AlwaysOnTop = d.AlwaysOnTop;
        _state.AutoUpdateSelf = d.AutoUpdateSelf;
        _state.AllowPreRelease = d.AllowPreRelease;
        _state.ArchOverride = d.ArchOverride;
        _state.AccentColor = d.AccentColor;
        _state.ShowWhatsNew = d.ShowWhatsNew;
        StateStore.Save(_state);

        Settings.Load(_state);
        ApplyAccent();
        ApplyAlwaysOnTop();
        ApplyCheckInterval();
        foreach (var arch in ArchInfo.Supported) PruneCache(arch);
        Builds.Refresh();
        RefreshUndo();
        Status.Set(Strings.ResetDone, Strings.ResetDoneDetail, Phase.Good);
        await RunGuardedAsync(CheckFlowAsync); // CPU type or pre-release choice may have changed
    }

    /// <summary>"Stop skipping": the skipped build can be offered again, so look again right away.</summary>
    private async Task ClearSkipAsync()
    {
        if (_busy || _state.SkippedBuild is not { } folder) return;
        _state.SkippedBuild = null;
        StateStore.Save(_state);
        Builds.SetSkipped(null);
        await RunGuardedAsync(CheckFlowAsync);
        if (_pending is null) Status.Set(Strings.SkipCleared, Strings.SkipClearedDetail(folder), Phase.Good);
    }
}
