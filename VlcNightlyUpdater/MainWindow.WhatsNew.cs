using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>
/// The full "What's new" dialogs. Before updating this updater: notes come from the GitHub release (so 5.0.1 and newer
/// releases can show it; older versions don't have this code). After an update: the CHANGELOG.md that shipped with this
/// copy. Wording lives in Strings.cs; the notes themselves live in CHANGELOG.md.
/// </summary>
public sealed partial class MainWindow
{
    private string? _promptedThisRun; // updater version already offered during this run (no nagging every 4 hours)

    /// <summary>Shows the notes of a newer updater. True = the user pressed the main button (update / open download page).</summary>
    private async Task<bool> ShowUpdateDialogAsync(SelfUpdateInfo info, bool canInstall)
    {
        var dialog = new WhatsNewDialog(
            Root.XamlRoot,
            Strings.UpdateDialogTitle(info.Version),
            Strings.SelfUpdateRange(info.Version),
            info.Notes.Length > 0 ? info.Notes : Strings.NoNotes,
            canInstall ? Strings.UpdateAndRestart : Strings.OpenDownloadPage,
            Strings.Later);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>First launch after the updater was updated: show what changed, once. Fresh installs stay quiet.</summary>
    private async Task ShowJustUpdatedAsync()
    {
        var now = Net.CurrentVersion.ToString();
        var before = _state.LastSeenVersion;
        if (before == now) return;

        _state.LastSeenVersion = now;
        StateStore.Save(_state);

        // before == null: a fresh install, or a state file from a version that predates this feature.
        if (before is null || !_state.ShowWhatsNew) return;

        var notes = ReleaseNotes.LoadBundled();
        if (notes.Length == 0) return;

        try
        {
            await new WhatsNewDialog(Root.XamlRoot, Strings.JustUpdatedTitle(Net.CurrentVersion),
                Strings.JustUpdatedRange(before, now), notes, primary: null, Strings.GotIt).ShowAsync();
        }
        catch { /* a dialog must never stop the update check */ }
    }

    /// <summary>The "What's new in this version" link in Settings.</summary>
    private async Task ShowCurrentNotesAsync()
    {
        var notes = ReleaseNotes.LoadBundled();
        try
        {
            await new WhatsNewDialog(Root.XamlRoot, Strings.WhatsNewTitle(Net.CurrentVersion), $"v{Net.CurrentVersion}",
                notes.Length > 0 ? notes : Strings.NoNotes, primary: null, Strings.Close).ShowAsync();
        }
        catch { }
    }
}
