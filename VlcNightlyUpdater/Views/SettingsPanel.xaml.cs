using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater.Views;

/// <summary>
/// Reads/writes AppState for every setting. To add a setting: AppState property + a ToggleRow in the .xaml
/// + two lines here (load it, save it). Raises RecheckRequested when a change means "look for updates again".
/// </summary>
public sealed partial class SettingsPanel : UserControl
{
    private AppState _state = new();
    private bool _loading;

    public event EventHandler? RecheckRequested;

    public SettingsPanel()
    {
        InitializeComponent();

        AutoRow.Toggled += (_, _) => Save(s => s.AutoUpdate = AutoRow.IsOn);
        LaunchRow.Toggled += (_, _) => Save(s => s.LaunchAfterUpdate = LaunchRow.IsOn);
        SelfRow.Toggled += (_, _) => Save(s => s.AutoUpdateSelf = SelfRow.IsOn);
        PreRow.Toggled += (_, _) =>
        {
            Save(s => s.AllowPreRelease = PreRow.IsOn);
            RecheckRequested?.Invoke(this, EventArgs.Empty); // new setting changes which updater version counts as "newest"
        };
        BgRow.Toggled += async (_, _) => await ApplyBackgroundAsync(BgRow.IsOn);
    }

    public void Load(AppState state)
    {
        _state = state;
        _loading = true;
        AutoRow.IsOn = state.AutoUpdate;
        LaunchRow.IsOn = state.LaunchAfterUpdate;
        SelfRow.IsOn = state.AutoUpdateSelf;
        PreRow.IsOn = state.AllowPreRelease;
        BgRow.IsOn = state.BackgroundCheck;
        BuildArchPicker();
        _loading = false;

        RepoLink.NavigateUri = new Uri(AppConfig.RepoUrl);
        ReleasesLink.NavigateUri = new Uri(AppConfig.RepoUrl + "/releases");
        SyncBackground();
    }

    /// <summary>Disables the pickers while an action runs.</summary>
    public void SetBusy(bool busy) => ArchPicker.IsEnabled = !busy;

    private void Save(Action<AppState> change)
    {
        if (_loading) return;
        change(_state);
        StateStore.Save(_state);
    }

    // ---------- VLC build (ARM64 PCs only) ----------

    private void BuildArchPicker()
    {
        ArchCard.Visibility = ArchInfo.Supported.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ArchPicker.Items.Clear();
        ArchPicker.Items.Add(new ComboBoxItem { Content = "Auto (match the installed VLC)", Tag = "auto" });
        foreach (var a in ArchInfo.Supported)
            ArchPicker.Items.Add(new ComboBoxItem { Content = a.Label(), Tag = a.Key() });

        var want = ArchInfo.ParseKey(_state.ArchOverride)?.Key() ?? "auto";
        ArchPicker.SelectedItem = ArchPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == want)
                                  ?? ArchPicker.Items[0];
    }

    private void ArchPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ArchPicker.SelectedItem is not ComboBoxItem { Tag: string key }) return;
        if (_state.ArchOverride == key) return;
        Save(s => s.ArchOverride = key);
        RecheckRequested?.Invoke(this, EventArgs.Empty); // different channel: look again
    }

    // ---------- optional background check ----------

    private string BackgroundNote(bool on) => on ? Strings.BackgroundOn : Strings.BackgroundOff;

    /// <summary>Makes Windows match the saved switch (OFF removes any leftover task/keys). Runs off the UI thread.</summary>
    private void SyncBackground()
    {
        if (!SelfUpdater.IsInstalledCopy())
        {
            // A build-folder copy can't own a scheduled task: it would point at a path that goes away.
            BgRow.IsEnabled = false;
            BgRow.Hint = Strings.BackgroundInstalledOnly;
            return;
        }

        BgRow.Hint = BackgroundNote(_state.BackgroundCheck);
        var on = _state.BackgroundCheck;
        _ = Task.Run(() =>
        {
            try { if (on) BackgroundCheck.Enable(); else BackgroundCheck.Disable(); }
            catch { /* best-effort refresh; the toggle itself reports errors */ }
        });
    }

    private async Task ApplyBackgroundAsync(bool on)
    {
        BgRow.IsEnabled = false;
        try
        {
            await Task.Run(() => { if (on) BackgroundCheck.Enable(); else BackgroundCheck.Disable(); });
            _state.BackgroundCheck = on;
            if (!on) _state.LastNotifiedBuild = null;
            StateStore.Save(_state);
            BgRow.Hint = BackgroundNote(on);
        }
        catch (Exception ex)
        {
            BgRow.IsOn = !on; // quiet revert
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = on ? Strings.BackgroundOnFailed : Strings.BackgroundOffFailed,
                Content = ex.Message,
                CloseButtonText = Strings.Ok
            }.ShowAsync();
        }
        finally
        {
            BgRow.IsEnabled = true;
        }
    }
}
