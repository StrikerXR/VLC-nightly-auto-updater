using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>
/// "Undo last update": go back to the build we just replaced. Uses the cached installer when there is one (works
/// offline), otherwise downloads that build again. Only offered for the same CPU type, so it can never switch
/// architecture by accident. After an undo, the build we backed out of is "skipped" so auto-update doesn't put it
/// straight back. Wording lives in Strings.cs; the button lives in Views/ActionBar.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>What Undo would do right now, or null when it isn't possible.</summary>
    private (string Back, VlcArch Arch)? GetUndo()
    {
        var back = _state.PreviousBuild;
        var now = _state.InstalledBuild;
        if (back is null || now is null || back == now) return null;

        var current = ArchInfo.ParseKey(_state.InstalledArch);
        if (current is null) return null;

        var before = ArchInfo.ParseKey(_state.PreviousArch);
        if (before is null)
        {
            // State written by 5.0.0 doesn't know the CPU type of the previous build. Only trust a cached installer.
            if (!InstallerCache.Has(back, current.Value)) return null;
            before = current;
        }
        return before == current ? (back, current.Value) : null;
    }

    private void RefreshUndo() => Actions.SetUndo(GetUndo() is { } u ? u.Back : null, busy: _busy);

    private async Task UndoLastUpdateAsync()
    {
        if (GetUndo() is not { } undo) return;
        var undone = _state.InstalledBuild!;
        var offline = InstallerCache.Has(undo.Back, undo.Arch);

        if (!await ConfirmAsync(Strings.UndoTitle(undo.Back), Strings.UndoBody(undone, undo.Back, offline), Strings.UndoConfirm, yesIsDefault: true))
            return;

        _target = undo.Arch;
        _updates.Arch = undo.Arch;
        await InstallFolderAsync(undo.Back);

        // Only if it really went back: remember what we backed out of, and stop offering to undo.
        if (_state.InstalledBuild != undo.Back) return;
        _state.SkippedBuild = undone;
        _state.PreviousBuild = _state.PreviousHash = _state.PreviousArch = null;
        StateStore.Save(_state);
        Status.Set(Strings.UndoDone, Strings.UndoDoneDetail(undo.Back, undone), Phase.Good);
    }
}
